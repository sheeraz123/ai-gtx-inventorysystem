using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class NumberingStore(DevelopmentDatabase database) : INumberingStore
{
    public async Task<NumberingResult> ExecuteAsync(NumberingRequest request,AuthenticatedIdentity actor,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var validity=await ScopedAccess.VerifyAsync(db,database.ExpectedInstallationId(),actor,ct);
            if(validity!=CompanyOutcome.Success) return new(validity);
            var company=await db.Set<Company>().Where(c=>c.IsActive && db.Set<CompanySelection>()
                .Any(s=>s.CompanyId==c.Id)).SingleOrDefaultAsync(ct);
            if(company is null) return Invalid("Select an active company first.");
            if(request.Company!=company.Id && (request.Company.HasValue || request.Action!=NumberingAction.List))
                return new(CompanyOutcome.Conflict,Message:"The current company changed. Refresh the list.");
            var admin=await ScopedAccess.IsSuperAdminAsync(db,actor.UserId,ct);
            var permissions=admin?null:await ScopedAccess.ResolveAsync(db,actor.UserId,company.Id,ct);
            var module=request.Master==NumberingMaster.Years?"FinancialYear.":"DocumentSeries.";
            bool Can(string action)=>admin || permissions!.Contains(module+action);
            var required=request.Action switch
            {
                NumberingAction.Save=>request.Id.HasValue?"Edit":"Create",
                NumberingAction.SetCurrent=>"SetCurrent",NumberingAction.Close=>"Close",NumberingAction.Reopen=>"Reopen",
                NumberingAction.SetDefault=>"SetDefault",NumberingAction.Status=>"StatusChange",_=>"View"
            };
            if(!Can("View") || !Can(required)) return new(CompanyOutcome.Forbidden);
            var catalog=new CompanyNumbering(db,company,actor.UserId);
            var result=await catalog.ExecuteAsync(request,ct);
            if(result.Outcome==CompanyOutcome.Success && request.Action is not
                (NumberingAction.List or NumberingAction.View or NumberingAction.Options or NumberingAction.Preview))
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return result;
        }
        catch(DbUpdateConcurrencyException) {return new(CompanyOutcome.Conflict);}
        catch(DbUpdateException e) when(e.InnerException is SqliteException {SqliteErrorCode:19})
        {return new(CompanyOutcome.Conflict,Message:"A duplicate or conflicting year or series was rejected.");}
        catch(Exception e) when(e is SqliteException or DbUpdateException or InvalidOperationException or IOException)
        {return new(CompanyOutcome.Unavailable);}
    }
    internal static NumberingResult Invalid(string message)=>new(CompanyOutcome.InvalidInput,Message:message);
}

internal sealed class CompanyNumbering(BillingDbContext db,Company company,Guid actor)
{
    private IQueryable<FinancialYear> Years=>db.Set<FinancialYear>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<DocumentNumberingSeries> Series=>db.Set<DocumentNumberingSeries>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<GstRegistration> Registrations=>db.Set<GstRegistration>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<NumberCounter> Counters=>db.Set<NumberCounter>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<DocumentNumberLedger> Ledger=>db.Set<DocumentNumberLedger>().Where(x=>x.CompanyId==company.Id);
    private static NumberingResult Invalid(string message)=>NumberingStore.Invalid(message);

    internal async Task<NumberingResult> ExecuteAsync(NumberingRequest request,CancellationToken ct)
    {
        if(request.Action==NumberingAction.List) return await ListAsync(request,ct);
        if(request.Action==NumberingAction.Options)
        {
            var years=await Years.Where(x=>x.Status=="Open" || Series.Any(s=>s.Id==request.Id && s.FinancialYearId==x.Id))
                .OrderByDescending(x=>x.Start).Select(x=>new NumberingChoice(x.Id,x.Code+" — "+x.Name)).ToArrayAsync(ct);
            var registrations=await Registrations.Where(x=>x.IsActive || Series.Any(s=>s.Id==request.Id && s.GstRegistrationId==x.Id))
                .OrderBy(x=>x.Gstin).Select(x=>new NumberingChoice(x.Id,x.Gstin)).ToArrayAsync(ct);
            return new(CompanyOutcome.Success,company.Id,Years:years,Registrations:registrations);
        }
        return request.Master==NumberingMaster.Years?await YearAsync(request,ct):await SeriesAsync(request,ct);
    }

    private async Task<NumberingResult> ListAsync(NumberingRequest request,CancellationToken ct)
    {
        var search=CatalogRules.Normalize(request.Search);
        if(search.Length<3) search="";
        var current=await Years.Where(x=>x.IsCurrent).Select(x=>x.Code+" — "+x.Name).SingleOrDefaultAsync(ct);
        if(request.Master==NumberingMaster.Years)
        {
            var years=await Years.Where(x=>(request.Active==null || (x.Status=="Open")==request.Active) &&
                (search=="" || x.Code.Contains(search) || x.Name.ToUpper().Contains(search)))
                .OrderByDescending(x=>x.Start).ToArrayAsync(ct);
            return new(CompanyOutcome.Success,company.Id,Rows:years.Select(Describe).ToArray(),CurrentYear:current);
        }

        var types=DocumentTypes.All.Where(x=>x.Label.Contains(search,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Code).ToArray();
        var rows=await (from s in Series join y in Years on s.FinancialYearId equals y.Id
            where (request.Active==null || s.IsActive==request.Active) &&
                (search=="" || s.Code.Contains(search) || s.Name.ToUpper().Contains(search) ||
                 s.Prefix.ToUpper().Contains(search) || s.DocumentType.ToUpper().Contains(search) || types.Contains(s.DocumentType))
            orderby y.Start descending,s.Code
            select new {Series=s,Year=y.Code,Next=Counters.Where(c=>c.SeriesId==s.Id && c.FinancialYearId==y.Id)
                .Select(c=>(long?)c.NextNumber).SingleOrDefault()}).ToArrayAsync(ct);
        return new(CompanyOutcome.Success,company.Id,Rows:rows.Select(x=>Describe(x.Series,x.Year,x.Next)).ToArray(),CurrentYear:current);
    }

    private async Task<NumberingResult> YearAsync(NumberingRequest request,CancellationToken ct)
    {
        var row=await Years.SingleOrDefaultAsync(y=>y.Id==request.Id,ct);
        if(request.Id.HasValue && row is null) return new(CompanyOutcome.NotFound);
        if(request.Action==NumberingAction.View)
        {
            if(row is null) return new(CompanyOutcome.NotFound);
            var series=await Series.Where(x=>x.FinancialYearId==row.Id).OrderBy(x=>x.Code).Take(21)
                .Select(x=>x.Code+" — "+x.Name).ToArrayAsync(ct);
            return Result(Describe(row) with {Details=series.Length==0?"No document series configured.":
                string.Join(Environment.NewLine,series.Take(20))+(series.Length>20?Environment.NewLine+"More series exist.":"")});
        }
        if(request.Entry is not { } input) return Invalid("Provide financial year details.");
        if(row is not null && row.Version!=input.Version) return new(CompanyOutcome.Conflict);
        string? reason=null;
        string action;
        var now=DateTime.UtcNow;
        if(request.Action==NumberingAction.Save)
        {
            var errors=new Dictionary<string,string>();
            CatalogRules.ValidateIdentity(input.Code,input.Name,errors);
            if(errors.Count>0 || input.Start is not DateOnly start || input.End is not DateOnly end || end<=start)
                return Invalid("Enter a valid code and name and an end date after the start date.");
            var code=CatalogRules.Normalize(input.Code);
            if(row is not null && row.Code!=code) return Invalid("The year code is fixed after creation.");
            if(await Years.AnyAsync(x=>x.Id!=request.Id && (x.Code==code || start<=x.End && x.Start<=end),ct))
                return Invalid("The financial year code or date range conflicts with another year.");
            if(row is not null && (row.Start!=start || row.End!=end) &&
                (row.Status!="Open" || await Series.AnyAsync(x=>x.FinancialYearId==row.Id,ct) ||
                await Counters.AnyAsync(x=>x.FinancialYearId==row.Id,ct) || await Ledger.AnyAsync(x=>x.FinancialYearId==row.Id,ct)))
                return Invalid("Dates are fixed once a financial year is closed or referenced.");
            action=row is null?"Created":"Updated";
            if(row is null) {row=new FinancialYear {CompanyId=company.Id,Code=code,CreatedUtc=now,CreatedBy=actor};db.Add(row);}
            row.Name=input.Name.Trim();row.Start=start;row.End=end;
        }
        else
        {
            if(row is null) return new(CompanyOutcome.NotFound);
            switch(request.Action)
            {
                case NumberingAction.SetCurrent when row.Status=="Open":
                    var previous=await Years.SingleOrDefaultAsync(x=>x.IsCurrent && x.Id!=row.Id,ct);
                    if(previous is not null)
                    {
                        previous.IsCurrent=false;previous.UpdatedUtc=now;previous.UpdatedBy=actor;
                        await db.SaveChangesAsync(ct);
                    }
                    row.IsCurrent=true;action="CurrentChanged";break;
                case NumberingAction.Close when row.Status=="Open":
                    row.Status="Closed";row.IsCurrent=false;row.ClosedUtc=now;row.ClosedBy=actor;action="AdministrativelyClosed";break;
                case NumberingAction.Reopen when row.Status=="Closed":
                    reason=CatalogRules.Optional(input.Reason);
                    if(reason is null || reason.Length>500 || reason.Any(char.IsControl)) return Invalid("Enter a reopening reason of up to 500 characters without control characters.");
                    row.Status="Open";row.ReopenedUtc=now;row.ReopenedBy=actor;row.ReopenReason=reason;action="Reopened";break;
                default:return Invalid("The requested action is unavailable for this financial year.");
            }
        }
        row.UpdatedUtc=now;row.UpdatedBy=actor;
        Audit(row.Id,"FinancialYear",action,new {row.Code,row.Start,row.End,row.Status,row.IsCurrent,Reason=reason});
        return new(CompanyOutcome.Success,company.Id);
    }

    private async Task<NumberingResult> SeriesAsync(NumberingRequest request,CancellationToken ct)
    {
        var row=await Series.SingleOrDefaultAsync(s=>s.Id==request.Id,ct);
        if(request.Id.HasValue && row is null) return new(CompanyOutcome.NotFound);
        var counter=row is null?null:await Counters.Where(x=>x.SeriesId==row.Id && x.FinancialYearId==row.FinancialYearId)
            .Select(x=>(long?)x.NextNumber).SingleOrDefaultAsync(ct);
        var used=row is not null && (counter.HasValue || await Ledger.AnyAsync(x=>x.SeriesId==row.Id,ct));
        var oldYearId=row?.FinancialYearId;
        var oldYear=await Years.SingleOrDefaultAsync(x=>x.Id==oldYearId,ct);
        if(request.Action==NumberingAction.View)
            return row is null?new(CompanyOutcome.NotFound):Result(Describe(row,oldYear?.Code,counter) with {Used=used});
        if(request.Entry is not { } input) return Invalid("Provide series details.");
        if(row is not null && row.Version!=input.Version) return new(CompanyOutcome.Conflict);
        var now=DateTime.UtcNow;
        string action;
        if(request.Action is NumberingAction.Save or NumberingAction.Preview)
        {
            var result=await ValidateAsync(row,input,used,counter,request.Action==NumberingAction.Save,ct);
            if(result.Outcome!=CompanyOutcome.Success || request.Action==NumberingAction.Preview) return result;
            var year=input.YearId!.Value;var gst=input.RegistrationId!.Value;
            action=row is null?"Created":"Updated";
            if(row is null)
            {
                row=new DocumentNumberingSeries {CompanyId=company.Id,FinancialYearId=year,GstRegistrationId=gst,
                    Code=CatalogRules.Normalize(input.Code),DocumentType=input.DocumentType,CreatedUtc=now,CreatedBy=actor};
                db.Add(row);
            }
            row.Name=input.Name.Trim();row.Prefix=input.Prefix;row.Separator=input.Separator;
            row.Suffix=input.Suffix;row.Padding=input.Padding;row.StartingNumber=input.Starting;
        }
        else
        {
            if(row is null) return new(CompanyOutcome.NotFound);
            switch(request.Action)
            {
                case NumberingAction.SetDefault when row.IsActive && oldYear?.Status=="Open":
                    if(!await Registrations.AnyAsync(x=>x.Id==row.GstRegistrationId && x.IsActive,ct)) return Invalid("Choose a series with an active GST registration.");
                    var validation=await ValidateAsync(row,Describe(row,oldYear.Code,counter),used,counter,true,ct);
                    if(validation.Outcome!=CompanyOutcome.Success) return validation;
                    var previous=await Series.SingleOrDefaultAsync(x=>x.Id!=row.Id && x.IsDefault &&
                        x.FinancialYearId==row.FinancialYearId && x.GstRegistrationId==row.GstRegistrationId && x.DocumentType==row.DocumentType,ct);
                    if(previous is not null)
                    {
                        previous.IsDefault=false;previous.UpdatedUtc=now;previous.UpdatedBy=actor;
                        await db.SaveChangesAsync(ct);
                    }
                    row.IsDefault=true;action="DefaultChanged";break;
                case NumberingAction.Status:
                    if(input.Active)
                    {
                        var check=await ValidateAsync(row,Describe(row,oldYear?.Code,counter),used,counter,true,ct);
                        if(check.Outcome!=CompanyOutcome.Success) return check;
                    }
                    row.IsActive=input.Active; if(!row.IsActive) row.IsDefault=false;
                    action=row.IsActive?"Activated":"Deactivated";break;
                default:return Invalid("The requested action is unavailable for this series.");
            }
        }
        row.UpdatedUtc=now;row.UpdatedBy=actor;
        Audit(row.Id,"DocumentSeries",action,new {row.Code,row.FinancialYearId,row.DocumentType,row.IsActive,row.IsDefault});
        return new(CompanyOutcome.Success,company.Id);
    }

    private async Task<NumberingResult> ValidateAsync(DocumentNumberingSeries? row,NumberingEntry input,bool used,
        long? counter,bool save,CancellationToken ct)
    {
        var errors=new Dictionary<string,string>();
        CatalogRules.ValidateIdentity(input.Code,input.Name,errors);
        if(row is not null && row.Code.StartsWith("LEG-",StringComparison.Ordinal) && input.Code==row.Code)
            errors.Remove("Code");
        var year=await Years.SingleOrDefaultAsync(x=>x.Id==input.YearId,ct);
        if(errors.Count>0 || year is null || !await Registrations.AnyAsync(x=>x.Id==input.RegistrationId && (x.IsActive || !save),ct) ||
            (!DocumentTypes.All.Any(x=>x.Code==input.DocumentType) && row?.DocumentType!=input.DocumentType))
            return Invalid("Enter a valid code and name, financial year, GST registration and document type.");
        if(save && year.Status!="Open") return Invalid("A closed financial year cannot configure an issuing series.");
        var code=CatalogRules.Normalize(input.Code);
        if(row is not null && (row.FinancialYearId!=year.Id || row.GstRegistrationId!=input.RegistrationId ||
            row.DocumentType!=input.DocumentType || row.Code!=code))
            return Invalid("The series code, year, GST registration and document type are fixed after creation.");
        if(used && row is not null && (row.Prefix!=input.Prefix || row.Separator!=input.Separator || row.Suffix!=input.Suffix ||
            row.Padding!=input.Padding || row.StartingNumber!=input.Starting))
            return Invalid("Format and starting number are locked once a counter or ledger entry exists.");
        var format=DocumentNumberFormat.Create(input with {Code=code},company.Code,year.Code);
        var next=counter??input.Starting;
        if(format is null || input.Starting<1 || next<1 || next>format.Maximum || input.Starting>format.Maximum)
            return Invalid("Use only approved tokens, letters, digits, slash and hyphen. The number must fit within 16 characters and the remaining digit range.");
        var excluded=row?.Id;
        var scope=Series.Where(x=>x.FinancialYearId==year.Id && x.GstRegistrationId==input.RegistrationId && x.Id!=excluded);
        if(await scope.AnyAsync(x=>x.Code==code && x.DocumentType==input.DocumentType,ct))
            return Invalid("This series code already exists in the selected document scope.");
        var peers=await scope.ToArrayAsync(ct);
        if(peers.Any(x=>DocumentNumberFormat.Create(Describe(x,year.Code,null),company.Code,year.Code) is not { } peer || format.Overlaps(peer)))
            return Invalid("The format may collide with another series in this year and GST registration. Use a distinct prefix or suffix.");
        var issued=await Ledger.Where(x=>x.FinancialYearId==year.Id && x.GstRegistrationId==input.RegistrationId &&
            x.SeriesId!=excluded).Select(x=>x.CompleteNumber).ToArrayAsync(ct);
        if(issued.Any(format.CouldRender)) return Invalid("The format may reuse a number retained in the permanent ledger.");
        return new(CompanyOutcome.Success,company.Id,Preview:format.Render(next));
    }

    private void Audit(Guid id,string type,string action,object details)=>db.Add(new AuditEvent
    {
        CompanyId=company.Id,ActorUserId=actor,ActorKind="User",RecordId=id.ToString(),RecordType=type,
        Action=type+action,ChangesJson=JsonSerializer.Serialize(details)
    });
    private NumberingResult Result(NumberingEntry row)=>new(CompanyOutcome.Success,company.Id,Detail:row);
    private static NumberingEntry Describe(FinancialYear x)=>new(x.Id,x.Version,x.Code,x.Name,x.Start,x.End,
        x.Status=="Open",x.IsCurrent,CreatedUtc:x.CreatedUtc,UpdatedUtc:x.UpdatedUtc,ClosedUtc:x.ClosedUtc,
        ReopenedUtc:x.ReopenedUtc,Reason:x.ReopenReason);
    private static NumberingEntry Describe(DocumentNumberingSeries x,string? year,long? counter)=>new(x.Id,x.Version,x.Code,x.Name,
        YearId:x.FinancialYearId,RegistrationId:x.GstRegistrationId,DocumentType:x.DocumentType,Prefix:x.Prefix,
        Separator:x.Separator,Padding:x.Padding,Starting:x.StartingNumber,Next:counter??x.StartingNumber,Suffix:x.Suffix,
        Active:x.IsActive,IsDefault:x.IsDefault,YearLabel:year,CreatedUtc:x.CreatedUtc,UpdatedUtc:x.UpdatedUtc);
}
