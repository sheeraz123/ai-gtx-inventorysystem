using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class TaxCatalogStore(DevelopmentDatabase database) : ITaxCatalogStore
{
    public async Task<TaxResult> ExecuteAsync(TaxRequest request,AuthenticatedIdentity actor,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var valid=await ScopedAccess.VerifyAsync(db,database.ExpectedInstallationId(),actor,ct);
            if(valid!=CompanyOutcome.Success) return new(valid);
            var company=await db.Set<CompanySelection>().Where(s=>db.Set<Company>()
                .Any(c=>c.Id==s.CompanyId && c.IsActive)).Select(s=>(Guid?)s.CompanyId).SingleOrDefaultAsync(ct);
            if(company is not Guid id) return Invalid("Select an active company first.");
            if(request.Company!=company && (request.Company.HasValue || request.Action!=TaxAction.List))
                return new(CompanyOutcome.Conflict,Message:"The active company changed. Refresh and try again.");
            var administrator=await ScopedAccess.IsSuperAdminAsync(db,actor.UserId,ct);
            var grants=administrator ? null : await ScopedAccess.ResolveAsync(db,actor.UserId,id,ct);
            bool Can(string code)=>administrator || grants!.Contains(code);
            var module=request.Master switch { TaxMaster.Classifications=>"TaxClassification",TaxMaster.Product=>"Product",_=>"GstRate" };
            if(!Can(module+".View")) return new(CompanyOutcome.Forbidden);
            if(request.Action==TaxAction.Status &&
                (request.Master is TaxMaster.Assignments or TaxMaster.Product || !Can(module+".StatusChange")))
                return new(CompanyOutcome.Forbidden);
            if(request.Action==TaxAction.Save && (request.Master switch
            {
                TaxMaster.Assignments=>!Can("GstRate.Assign"),
                TaxMaster.Product=>!Can("Product.Edit") || !Can("GstRate.Assign"),
                _=>!Can(module+(request.Id.HasValue?".Edit":".Create"))
            })) return new(CompanyOutcome.Forbidden);
            var catalog=new CompanyTaxCatalog(db,id,Can("Product.View"));
            var outcome=await catalog.ExecuteAsync(request,actor.UserId,ct);
            if(outcome.Outcome==CompanyOutcome.Success && request.Action is TaxAction.Save or TaxAction.Status)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return outcome;
        }
        catch(DbUpdateConcurrencyException) { return new(CompanyOutcome.Conflict); }
        catch(DbUpdateException e) when(e.InnerException is SqliteException {SqliteErrorCode:19})
        { return new(CompanyOutcome.Conflict,Message:"A duplicate, overlapping range or conflicting link was rejected."); }
        catch(Exception e) when(e is SqliteException or DbUpdateException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
    }

    internal static TaxResult Invalid(string message)=>new(CompanyOutcome.InvalidInput,Message:message);
}

internal sealed class CompanyTaxCatalog(BillingDbContext db,Guid company,bool canViewProducts=false)
{
    private IQueryable<TaxClassification> Classes=>db.Set<TaxClassification>().Where(x=>x.CompanyId==company);
    private IQueryable<GstRateDefinition> Rates=>db.Set<GstRateDefinition>().Where(x=>x.CompanyId==company);
    private IQueryable<TaxClassificationRateAssignment> Assignments=>db.Set<TaxClassificationRateAssignment>().Where(x=>x.CompanyId==company);
    private static DateOnly Today=>DateOnly.FromDateTime(DateTime.Today);
    private static TaxResult Invalid(string message)=>TaxCatalogStore.Invalid(message);

    internal async Task<TaxResult> ExecuteAsync(TaxRequest request,Guid actor,CancellationToken ct)
    {
        if(request.Master==TaxMaster.Product) return await ProductAsync(request,actor,ct);
        if(request.Action==TaxAction.List) return await ListAsync(request,ct);
        if(request.Action==TaxAction.Options) return await OptionsAsync(request.Id,ct);
        if(request.Action is not (TaxAction.View or TaxAction.Save or TaxAction.Status)) return Invalid("Unsupported master action.");
        return request.Master switch
        {
            TaxMaster.Classifications=>await ClassificationAsync(request,actor,ct),
            TaxMaster.Rates=>await RateAsync(request,actor,ct),
            _=>await AssignmentAsync(request,actor,ct)
        };
    }

    private async Task<TaxResult> ListAsync(TaxRequest request,CancellationToken ct)
    {
        var query=CatalogRules.Normalize(request.Search);
        if(query.Length<3) query="";
        if(request.Master==TaxMaster.Classifications)
        {
            var classifications=Classes.Where(x=>(request.Active==null || x.Active==request.Active) &&
                (query=="" || x.Code.Contains(query) || x.Description.ToUpper().Contains(query) ||
                 x.ShortDescription!=null && x.ShortDescription.ToUpper().Contains(query)));
            var today=Today;
            var rows=await classifications.OrderBy(x=>x.Type).ThenBy(x=>x.Code).ToArrayAsync(ct);
            var live=await (from a in Assignments join r in Rates on a.RateId equals r.Id
                where a.Active && a.From<=today && (a.To==null || today<a.To) && r.From<=today && (r.To==null || today<r.To)
                select new {a.ClassificationId,a.Context,r.Code,r.Total,r.Treatment}).ToArrayAsync(ct);
            return new(CompanyOutcome.Success,company,Rows:rows.Select(x=>
            {
                var matches=live.Where(a=>a.ClassificationId==x.Id).Take(2).ToArray();
                var summary=matches.Length switch
                {
                    0=>"No current assignment",
                    1=>$"{matches[0].Context} · {matches[0].Code} · {matches[0].Total:G29}%",
                    _=>"Multiple contexts or assignments"
                };
                return Describe(x) with {Summary=summary};
            }).ToArray());
        }

        if(request.Master==TaxMaster.Rates)
        {
            var rows=await Rates.Where(x=>(request.Active==null || x.Active==request.Active) &&
                (query=="" || x.Code.Contains(query) || x.Name.ToUpper().Contains(query) ||
                 x.Description!=null && x.Description.ToUpper().Contains(query)))
                .OrderBy(x=>x.Code).ThenByDescending(x=>x.From).ToArrayAsync(ct);
            return new(CompanyOutcome.Success,company,Rows:rows.Select(Describe).ToArray());
        }

        var assignments=await (from a in Assignments join c in Classes on a.ClassificationId equals c.Id
            join r in Rates on a.RateId equals r.Id
            where (request.Active==null || a.Active==request.Active) &&
                (query=="" || a.Context.Contains(query) || c.Code.Contains(query) || r.Code.Contains(query))
            orderby c.Code, a.Context, a.From descending
            select new { Assignment=a, Classification=c.Type+" "+c.Code, Rate=r.Code }).ToArrayAsync(ct);
        return new(CompanyOutcome.Success,company,Rows:assignments.Select(x=>Describe(x.Assignment) with
            {Code=x.Classification,Name=x.Rate}).ToArray());
    }

    private async Task<TaxResult> ClassificationAsync(TaxRequest request,Guid actor,CancellationToken ct)
    {
        var old=await Classes.SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
        if(request.Id.HasValue && old is null) return new(CompanyOutcome.NotFound);
        if(request.Action==TaxAction.View)
        {
            if(old is null) return Detail(null);
            if(!canViewProducts) return Detail(Describe(old));
            var products=await (from link in db.Set<ProductTaxProfile>()
                join product in db.Set<Product>() on new {link.CompanyId,Id=link.ProductId} equals new {product.CompanyId,product.Id}
                where link.CompanyId==company && link.ClassificationId==old.Id
                orderby product.Code
                select product.Code+" — "+product.Name).Take(11).ToArrayAsync(ct);
            var summary=products.Length==0 ? "No linked products." :
                string.Join(Environment.NewLine,products.Take(10))+(products.Length>10?Environment.NewLine+"More products are linked.":"");
            return Detail(Describe(old) with {Summary=summary});
        }
        if(request.Entry is not { } input) return Invalid("Provide classification details.");
        if(old is not null && old.Version!=input.Version) return new(CompanyOutcome.Conflict);
        if(request.Action==TaxAction.Status)
            return old is null ? Invalid("Select a classification.") : Status(old,input.Active,actor);
        var type=CatalogRules.Normalize(input.Type);
        var code=input.Code.Trim();
        var description=CatalogRules.Optional(input.Description);
        if(!code.All(char.IsAsciiDigit) || !(type=="HSN" && (code.Length is 2 or 4 or 6 or 8) && !code.StartsWith("99",StringComparison.Ordinal) ||
            type=="SAC" && code.Length==6 && code.StartsWith("99",StringComparison.Ordinal)))
            return Invalid("Enter a 2, 4, 6 or 8 digit HSN, or a 6 digit SAC starting with 99. Verify the code against an official source.");
        if(description is null || description.Length>500 || input.Name.Length>120 || input.Notes?.Length>1000)
            return Invalid("Enter a description of up to 500 characters. Short description and notes have limits of 120 and 1,000.");
        if(old is not null && (old.Type!=type || old.Code!=code)) return Invalid("Classification type and code are fixed. Add a new classification.");
        if(await Classes.AnyAsync(x=>x.Type==type && x.Code==code && x.Id!=request.Id,ct)) return Invalid("This classification already exists.");
        var row=old ?? Add(new TaxClassification {Type=type,Code=code},actor);
        row.Description=description; row.ShortDescription=CatalogRules.Optional(input.Name); row.Notes=CatalogRules.Optional(input.Notes);
        return Changed(row,actor,old is null?"Created":"Updated");
    }

    private async Task<TaxResult> RateAsync(TaxRequest request,Guid actor,CancellationToken ct)
    {
        var old=await Rates.SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
        if(request.Id.HasValue && old is null) return new(CompanyOutcome.NotFound);
        if(request.Action==TaxAction.View) return Detail(old is null?null:Describe(old));
        if(request.Entry is not { } input) return Invalid("Provide rate details.");
        if(old is not null && old.Version!=input.Version) return new(CompanyOutcome.Conflict);
        if(request.Action==TaxAction.Status) return old is null ? Invalid("Select a rate.") : Status(old,input.Active,actor);
        var errors=new Dictionary<string,string>();
        CatalogRules.ValidateIdentity(input.Code,input.Name,errors);
        if(errors.Count>0 || input.Description?.Length>500) return Invalid("Enter a valid rate code, name and optional description up to 500 characters.");
        var code=CatalogRules.Normalize(input.Code);
        if(input.From is not DateOnly from || input.To is DateOnly end && end<=from)
            return Invalid("Enter a start date and an optional end date after it. The end date is excluded.");
        if(!Enum.IsDefined(input.Treatment) || new[] {input.Total,input.Cgst,input.Sgst,input.Igst,input.Cess,input.Utgst??0}
            .Any(x=>x<0 || x>100 || decimal.Round(x,4)!=x))
            return Invalid("Percentages must be between 0 and 100 with at most four decimal places.");
        if(input.Treatment==TaxTreatment.Taxable
            ? input.Total<=0 || input.Cgst+input.Sgst!=input.Total || input.Igst!=input.Total ||
              input.Utgst is decimal ut && input.Cgst+ut!=input.Total
            : input.Total!=0 || input.Cgst!=0 || input.Sgst!=0 || input.Igst!=0 || input.Cess!=0 || (input.Utgst??0)!=0)
            return Invalid("For standard taxable rates, CGST + SGST and IGST must match total GST. Optional CGST + UTGST must also match. Other treatments use separate zero-component records.");
        if(old is not null && (old.Code!=code || old.From!=from || old.Treatment!=input.Treatment || old.Total!=input.Total ||
            old.Cgst!=input.Cgst || old.Sgst!=input.Sgst || old.Igst!=input.Igst || old.Utgst!=input.Utgst || old.Cess!=input.Cess))
            return Invalid("A rate version's code, start date, treatment and percentages are fixed. Add a new version for a change.");
        if(old is not null && old.To!=input.To &&
            (input.To is not DateOnly replacement || replacement<=Today || old.To.HasValue && replacement>=old.To ||
             await Assignments.AnyAsync(x=>x.RateId==old.Id && (x.To==null || x.To>replacement),ct)))
            return Invalid("Only prospective shortening is allowed. Close linked assignments before shortening a rate.");
        if(await Rates.AnyAsync(x=>x.Id!=request.Id && x.Code==code &&
            (input.To==null || x.From<input.To) && (x.To==null || from<x.To),ct))
            return Invalid("Another version of this rate code overlaps. Close its interval first.");
        var row=old ?? Add(new GstRateDefinition {Code=code,From=from,Treatment=input.Treatment,Total=input.Total,
            Cgst=input.Cgst,Sgst=input.Sgst,Igst=input.Igst,Utgst=input.Utgst,Cess=input.Cess},actor);
        row.Name=input.Name.Trim(); row.Description=CatalogRules.Optional(input.Description); row.To=input.To;
        return Changed(row,actor,old is null?"Created":"Updated");
    }

    private async Task<TaxResult> AssignmentAsync(TaxRequest request,Guid actor,CancellationToken ct)
    {
        var old=await Assignments.SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
        if(request.Id.HasValue && old is null) return new(CompanyOutcome.NotFound);
        if(request.Action==TaxAction.View) return Detail(old is null?null:Describe(old));
        if(request.Entry is not { ClassificationId:Guid classification,RateId:Guid rate,From:DateOnly from } input)
            return Invalid("Choose a classification, rate version and start date.");
        if(old is not null && old.Version!=input.Version) return new(CompanyOutcome.Conflict);
        var context=CatalogRules.Normalize(input.Context);
        if(context.Length is <1 or >40 || context.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) ||
            input.To is DateOnly end && end<=from)
            return Invalid("Enter a context code using letters, numbers, hyphens or underscores and a valid date interval.");
        if(old is not null && (old.ClassificationId!=classification || old.RateId!=rate || old.Context!=context || old.From!=from ||
            old.To!=input.To && (input.To is not DateOnly replacement || replacement<=Today || old.To.HasValue && replacement>=old.To)))
            return Invalid("Only prospective shortening is allowed. Add a new assignment for other changes.");
        if(!await Classes.AnyAsync(x=>x.Id==classification && (old!=null || x.Active),ct))
            return Invalid("Choose an active classification from this company.");
        var definition=await Rates.SingleOrDefaultAsync(x=>x.Id==rate,ct);
        if(definition is null || old is null && !definition.Active || from<definition.From ||
            definition.To is DateOnly rateEnd && (input.To is null || input.To>rateEnd))
            return Invalid("Choose a same-company rate covering the entire assignment interval.");
        if(await Assignments.AnyAsync(x=>x.Id!=request.Id && x.ClassificationId==classification && x.Context==context &&
            (input.To==null || x.From<input.To) && (x.To==null || from<x.To),ct))
            return Invalid("Another assignment overlaps for this classification and context.");
        var row=old ?? Add(new TaxClassificationRateAssignment {ClassificationId=classification,RateId=rate,Context=context,From=from},actor);
        row.To=input.To;
        return Changed(row,actor,old is null?"Created":"Closed");
    }

    private async Task<TaxResult> ProductAsync(TaxRequest request,Guid actor,CancellationToken ct)
    {
        if(request.Id is null && request.Action is TaxAction.Options or TaxAction.Preview &&
            request.Entry is { Type:"Goods" or "Services" } unsaved)
        {
            var draftType=unsaved.Type=="Services"?"SAC":"HSN";
            if(request.Action==TaxAction.Options)
                return await ProductOptionsAsync(draftType,unsaved.ClassificationId,null,null,ct);
            if(request.Date is DateOnly date && unsaved.ClassificationId is Guid classificationId)
                return await ResolveAsync(classificationId,draftType,CatalogRules.Normalize(unsaved.Context),date,ct);
            return Invalid("Choose a classification, tax context and transaction date.");
        }
        var product=await db.Set<Product>().SingleOrDefaultAsync(x=>x.CompanyId==company && x.Id==request.Id,ct);
        if(product is null) return new(CompanyOutcome.NotFound);
        var profile=await db.Set<ProductTaxProfile>().SingleOrDefaultAsync(x=>x.CompanyId==company && x.ProductId==product.Id,ct);
        var type=product.Kind=="Services"?"SAC":"HSN";
        if(request.Action is TaxAction.View or TaxAction.Options)
        {
            var selected=request.Action==TaxAction.Options ? request.Entry?.ClassificationId : profile?.ClassificationId;
            var detail=new TaxEntry(product.Id,product.Version,product.Hsn??"",product.Name,type,
                ClassificationId:profile?.ClassificationId,Context:profile?.Context??"",Total:product.GstRate??0,
                Summary:product.GstRate is decimal legacy?$"Legacy rate: {legacy:G29}%":"Legacy rate: unset");
            return await ProductOptionsAsync(type,selected,profile?.ClassificationId,detail,ct);
        }
        if(request.Action==TaxAction.Preview)
        {
            if(request.Date is not DateOnly date) return Invalid("Choose the transaction date.");
            if(request.Entry is {ClassificationId:Guid previewId} preview)
                return await ResolveAsync(previewId,type,CatalogRules.Normalize(preview.Context),date,ct);
            return await ResolveProductAsync(product,date,ct);
        }
        if(request.Action!=TaxAction.Save || request.Entry is not {ClassificationId:Guid classification} input)
            return Invalid("Choose an HSN/SAC classification and an assigned tax context.");
        if(product.Version!=input.Version) return new(CompanyOutcome.Conflict);
        var saved=await StageProductProfileAsync(product,profile,new(classification,input.Context),actor,ct);
        if(saved.Outcome!=CompanyOutcome.Success) return saved;
        product.UpdatedUtc=DateTime.UtcNow; product.UpdatedByUserId=actor;
        db.Entry(product).Property(x=>x.UpdatedUtc).IsModified=true;
        return saved;
    }

    internal async Task<TaxResult> StageProductProfileAsync(Product product,ProductTaxProfile? profile,
        ProductTaxSelection selection,Guid actor,CancellationToken ct)
    {
        var type=product.Kind=="Services"?"SAC":"HSN";
        var classification=selection.ClassificationId;
        var context=CatalogRules.Normalize(selection.Context);
        var currentClassification=profile is not null && profile.Context==context?profile.ClassificationId:(Guid?)null;
        if(!await Classes.AnyAsync(x=>x.Id==classification && x.Type==type && (x.Active || x.Id==currentClassification),ct) ||
            !await Assignments.AnyAsync(x=>x.ClassificationId==classification && x.Context==context && x.Active,ct))
            return Invalid("Choose a compatible same-company classification and assigned context.");
        if(profile is null)
        {
            profile=new ProductTaxProfile {CompanyId=company,ProductId=product.Id};
            db.Add(profile);
        }
        profile.ClassificationId=classification; profile.Context=context;
        Audit(product.Id,actor,"ProductTaxProfile","Updated");
        return new(CompanyOutcome.Success,company);
    }

    private async Task<TaxResult> ProductOptionsAsync(string type,Guid? selected,Guid? linked,TaxEntry? detail,CancellationToken ct)
    {
        var choices=await Classes.Where(x=>x.Type==type && (x.Active || x.Id==linked))
            .OrderBy(x=>x.Code).Select(x=>new TaxChoice(x.Id,x.Code+" — "+x.Description+(x.Active?"":" (inactive)"))).ToArrayAsync(ct);
        var contexts=await Assignments.Where(x=>x.ClassificationId==selected && x.Active).Select(x=>x.Context)
            .Distinct().OrderBy(x=>x).ToArrayAsync(ct);
        return new(CompanyOutcome.Success,company,Detail:detail,Classifications:choices,Contexts:contexts);
    }

    internal async Task<TaxResult> ResolveProductAsync(Product product,DateOnly date,CancellationToken ct)
    {
        var profiles=db.Set<ProductTaxProfile>();
        var profile=profiles.Local.SingleOrDefault(x=>x.CompanyId==company && x.ProductId==product.Id)
            ?? await profiles.SingleOrDefaultAsync(x=>x.CompanyId==company && x.ProductId==product.Id,ct);
        return profile is null ? Invalid($"Product {product.Code} has no linked HSN/SAC and GST profile.") :
            await ResolveAsync(profile.ClassificationId,product.Kind=="Services"?"SAC":"HSN",profile.Context,date,ct);
    }

    internal async Task<TaxResult> ResolveAsync(Guid classification,string type,string context,DateOnly date,CancellationToken ct)
    {
        var cls=await Classes.SingleOrDefaultAsync(x=>x.Id==classification && x.Type==type,ct);
        if(cls is null) return Invalid("Choose a tax classification compatible with the product type.");
        var matches=await (from a in Assignments join r in Rates on a.RateId equals r.Id
            where a.ClassificationId==classification && a.Context==context && a.Active &&
                a.From<=date && (a.To==null || date<a.To) && r.From<=date && (r.To==null || date<r.To)
            select new {a.Id,Rate=r}).Take(2).ToArrayAsync(ct);
        if(matches.Length!=1) return Invalid(matches.Length==0 ?
            "No applicable rate for this date and context." : "Multiple applicable rates. Review the assignments.");
        var item=matches[0]; var rate=item.Rate;
        return new(CompanyOutcome.Success,company,Snapshot:new(cls.Id,cls.Type,cls.Code,cls.Description,item.Id,rate.Id,
            rate.Code,rate.From,rate.Treatment,rate.Total,rate.Cgst,rate.Sgst,rate.Igst,rate.Utgst,rate.Cess,date,context));
    }

    private async Task<TaxResult> OptionsAsync(Guid? assignmentId,CancellationToken ct)
    {
        var assignment=await Assignments.SingleOrDefaultAsync(x=>x.Id==assignmentId,ct);
        var classificationId=assignment?.ClassificationId;
        var rateId=assignment?.RateId;
        var classes=await Classes.Where(x=>x.Active || x.Id==classificationId).OrderBy(x=>x.Code).Select(x=>new TaxChoice(x.Id,x.Type+" "+x.Code+" — "+x.Description+
            (x.Active?"":" (inactive)"))).ToArrayAsync(ct);
        var rates=await Rates.Where(x=>x.Active || x.Id==rateId).OrderBy(x=>x.Code).ThenByDescending(x=>x.From).ToArrayAsync(ct);
        return new(CompanyOutcome.Success,company,Classifications:classes,
            Rates:rates.Select(x=>new TaxChoice(x.Id,$"{x.Code} — {x.Name} ({x.From:yyyy-MM-dd})"+(x.Active?"":" (inactive)"))).ToArray());
    }

    private T Add<T>(T row,Guid actor) where T:TaxCatalogRecord
    {
        row.CompanyId=company; row.CreatedUtc=DateTime.UtcNow; row.CreatedByUserId=actor;
        db.Add(row);
        return row;
    }

    private TaxResult Status(TaxCatalogRecord row,bool active,Guid actor)
    {
        row.Active=active;
        return Changed(row,actor,active?"Activated":"Deactivated");
    }
    private TaxResult Changed(TaxCatalogRecord row,Guid actor,string action)
    {
        row.UpdatedUtc=DateTime.UtcNow; row.UpdatedByUserId=actor;
        Audit(row.Id,actor,db.Entry(row).Metadata.ClrType.Name,action);
        return new(CompanyOutcome.Success,company);
    }
    private void Audit(Guid id,Guid actor,string record,string action) =>
        db.Add(new AuditEvent {CompanyId=company,ActorUserId=actor,ActorKind="User",RecordId=id.ToString(),
            RecordType=record,Action=record+action,ChangesJson="{}"});
    private TaxResult Detail(TaxEntry? row) => row is null ? new(CompanyOutcome.NotFound) : new(CompanyOutcome.Success,company,Detail:row);
    private static TaxEntry Describe(TaxClassification x) =>
        new(x.Id,x.Version,x.Code,x.ShortDescription??"",x.Type,x.Description,x.Notes,x.Active);
    private static TaxEntry Describe(GstRateDefinition x) =>
        new(x.Id,x.Version,x.Code,x.Name,Description:x.Description,Active:x.Active,Treatment:x.Treatment,
            Total:x.Total,Cgst:x.Cgst,Sgst:x.Sgst,Igst:x.Igst,Utgst:x.Utgst,Cess:x.Cess,From:x.From,To:x.To);
    private static TaxEntry Describe(TaxClassificationRateAssignment x) =>
        new(x.Id,x.Version,Active:x.Active,From:x.From,To:x.To,ClassificationId:x.ClassificationId,RateId:x.RateId,Context:x.Context);
}
