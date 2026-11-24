using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class CompanyStore(DevelopmentDatabase database) : ICompanyStore
{
    public async Task<CompanyResult> ExecuteAsync(CompanyRequest request, AuthenticatedIdentity actor, CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var installation=await db.Set<Installation>().SingleAsync(ct);
            if(installation.Id!=database.ExpectedInstallationId() || installation.SetupState!="Configured" ||
                installation.DatabaseMode!="PlaintextDevelopment") return new(CompanyOutcome.Unavailable);
            var user=await db.Set<User>().SingleOrDefaultAsync(x=>x.Id==actor.UserId,ct);
            if(user?.IsActive!=true || user.IsLocked || user.MustChangePassword || user.Version!=actor.Version) return new(CompanyOutcome.SessionRequired);
            var admin=await ScopedAccess.IsSuperAdminAsync(db,actor.UserId,ct);
            if(!admin && request.Action is CompanyAction.Create or CompanyAction.SetActive) return new(CompanyOutcome.Forbidden);
            if(!admin) request=request with {ProfileOnly=true};
            var accessible=ScopedAccess.CompanyIds(db,actor.UserId);
            var selected=await db.Set<CompanySelection>().SingleOrDefaultAsync(ct);
            if(request.Action is CompanyAction.List or CompanyAction.Current)
            {
                var query=db.Set<Company>().AsNoTracking();
                if(!admin) query=query.Where(x=>accessible.Contains(x.Id));
                if(request.Action==CompanyAction.Current) query=query.Where(x=>selected!=null && x.Id==selected.CompanyId);
                var search=request.Search?.Trim() ?? "";
                if(search.Length>=3)
                {
                    if(search.Length>100) return new(CompanyOutcome.InvalidInput);
                    search=search.ToUpperInvariant();
                    query=query.Where(x=>x.Code.Contains(search) || x.LegalName.ToUpper().Contains(search) ||
                        x.DisplayName!=null && x.DisplayName.ToUpper().Contains(search) ||
                        admin && (db.Set<GstRegistration>().Any(g=>g.CompanyId==x.Id && g.IsActive && g.Gstin.ToUpper().Contains(search)) ||
                        db.Set<CompanyAddress>().Any(a=>a.CompanyId==x.Id && a.City.ToUpper().Contains(search))));
                }
                var pageSize=request.Action==CompanyAction.Current?1:request.PageSize;
                var companies=await query.OrderBy(x=>x.Code)
                    .Skip(request.Action==CompanyAction.Current?0:request.Page*pageSize).Take(pageSize+1).ToListAsync(ct);
                var rows=new List<CompanyDetails>();
                foreach(var company in companies.Take(pageSize))
                {
                    var view=admin || (await ScopedAccess.ResolveAsync(db,actor.UserId,company.Id,ct)).Contains("Company.View");
                    rows.Add(view ? await Read(db,company,selected?.CompanyId,false,ct,profileOnly:request.Action==CompanyAction.List || request.ProfileOnly)
                        : IdentityOnly(company,selected?.CompanyId));
                }
                return new(CompanyOutcome.Success,rows,More:companies.Count>pageSize);
            }
            Company companyRow;
            if(request.Action==CompanyAction.Create)
            {
                companyRow=new Company {CreatedByUserId=actor.UserId};
                db.Add(companyRow);
            }
            else
            {
                var found=await db.Set<Company>().SingleOrDefaultAsync(x=>x.Id==request.Id && (admin || accessible.Contains(x.Id)),ct);
                if(found is null) return new(CompanyOutcome.NotFound);
                companyRow=found;
                if(!admin && request.Action!=CompanyAction.Select)
                {
                    if(selected?.CompanyId!=companyRow.Id) return new(CompanyOutcome.Forbidden);
                    var permissions=await ScopedAccess.ResolveAsync(db,actor.UserId,companyRow.Id,ct);
                    if(!permissions.Contains("Company.View") || request.Action==CompanyAction.Update && !permissions.Contains("Company.Edit"))
                        return new(CompanyOutcome.Forbidden);
                }
                if(request.Action==CompanyAction.Get)
                    return new(CompanyOutcome.Success,[await Read(db,companyRow,selected?.CompanyId,true,ct,request.ProfileOnly)]);
                if(request.ExpectedVersion!=companyRow.Version) return new(CompanyOutcome.Conflict);
            }
            if(request.Action is CompanyAction.Create or CompanyAction.Update)
            {
                var draft=request.Draft!;
                if(request.Action==CompanyAction.Update && companyRow.Code!=draft.Code)
                    return new(CompanyOutcome.InvalidInput,Errors:new Dictionary<string,string>{{"Code","Company code is immutable."}});
                if(await db.Set<Company>().AnyAsync(x=>x.Id!=companyRow.Id && x.Code==draft.Code,ct))
                    return new(CompanyOutcome.Conflict,Errors:new Dictionary<string,string>{{"Code","Company code already exists."}});
                if(!request.ProfileOnly && draft.BankName is null && await db.Set<CompanyBankAccount>().AnyAsync(x=>x.CompanyId==companyRow.Id && x.IsActive && !x.IsDefault,ct))
                    return new(CompanyOutcome.InvalidInput,Errors:new Dictionary<string,string>{{"BankName","Other active accounts exist. Retain an eligible default bank account."}});
                ApplyCompany(companyRow,draft);
                // Save the parent first; foreign-key dependants and every later write share this transaction.
                await db.SaveChangesAsync(ct);
                await WriteChildren(db,companyRow,draft,request,actor.UserId,ct);
            }
            else if(request.Action==CompanyAction.SetActive)
            {
                if(!request.Active && selected?.CompanyId==companyRow.Id)
                {
                    db.Remove(selected);
                    await db.SaveChangesAsync(ct);
                    selected=null;
                }
                companyRow.IsActive=request.Active;
            }
            else if(request.Action==CompanyAction.Select)
            {
                if(!companyRow.IsActive) return new(CompanyOutcome.InvalidInput);
                if(selected is null) { selected=new CompanySelection {CompanyId=companyRow.Id}; db.Add(selected); }
                else selected.CompanyId=companyRow.Id;
            }
            else return new(CompanyOutcome.InvalidInput);
            if(request.Action!=CompanyAction.Select)
            {
                companyRow.UpdatedUtc=DateTime.UtcNow;
                companyRow.UpdatedByUserId=actor.UserId;
                db.Entry(companyRow).Property(x=>x.UpdatedUtc).IsModified=true;
            }
            db.Add(new AuditEvent {CompanyId=companyRow.Id,ActorUserId=actor.UserId,ActorKind="User",
                Action=request.Action switch {CompanyAction.Create=>"CompanyCreated",CompanyAction.Update=>"CompanyUpdated",
                    CompanyAction.Select=>"CurrentCompanySelected",_=>request.Active?"CompanyActivated":"CompanyDeactivated"},
                RecordType="Company",RecordId=companyRow.Id.ToString(),ChangesJson="{}"});
            await db.SaveChangesAsync(ct);
            var details = !admin && request.Action==CompanyAction.Select ? IdentityOnly(companyRow,selected?.CompanyId)
                : await Read(db,companyRow,selected?.CompanyId,true,ct,request.ProfileOnly);
            ct.ThrowIfCancellationRequested();
            await transaction.CommitAsync(ct);
            return new(CompanyOutcome.Success,[details]);
        }
        catch(OperationCanceledException) { throw; }
        catch(DbUpdateConcurrencyException) { return new(CompanyOutcome.Conflict); }
        catch(DbUpdateException e) when(e.InnerException is SqliteException {SqliteErrorCode:19})
        { return new(CompanyOutcome.Conflict); }
        catch(SqliteException e) when(e.SqliteErrorCode==19) { return new(CompanyOutcome.Conflict); }
        catch(Exception e) when(e is SqliteException or DbUpdateException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
    }

    private static CompanyDetails IdentityOnly(Company company,Guid? selected) => new(company.Id,company.Version,
        company.IsActive,selected==company.Id,new CompanyDraft { Code=company.Code,LegalName=company.LegalName,TradeName=company.DisplayName },
        company.CreatedUtc,null,null,null);

    private static void ApplyCompany(Company c,CompanyDraft d)
    {
        c.Code=d.Code; c.LegalName=d.LegalName; c.DisplayName=d.TradeName; c.Tagline=d.Tagline;
        c.Iec=d.Iec;
        c.BusinessType=d.BusinessType; c.EstablishedOn=d.EstablishedOn is null?null:DateOnly.ParseExact(d.EstablishedOn,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        c.RegistrationType=d.RegistrationType; c.Pan=d.Pan; c.Tan=d.Tan; c.Cin=d.Cin; c.Udyam=d.Udyam;
    }

    private static async Task<CompanyAddress?> Address(BillingDbContext db,Guid id,CancellationToken ct) =>
        await db.Set<CompanyAddress>().Where(x=>x.CompanyId==id)
            .OrderByDescending(x=>x.IsDefaultBilling).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
    private static async Task<GstRegistration?> Gst(BillingDbContext db,Guid id,Guid? address,CancellationToken ct)
    {
        if(address is null) return null;
        return await db.Set<GstRegistration>().Where(x=>x.CompanyId==id && x.IsActive &&
            db.Set<GstRegistrationAddress>().Any(m=>m.GstRegistrationId==x.Id && m.AddressId==address))
            .OrderBy(x=>x.Gstin).FirstOrDefaultAsync(ct);
    }
    private static async Task WriteChildren(BillingDbContext db,Company c,CompanyDraft d,CompanyRequest request,Guid actor,CancellationToken ct)
    {
        var address=await Address(db,c.Id,ct);
        var defaultDispatch=address?.IsDefaultDispatch ?? true;
        var gst=await Gst(db,c.Id,address?.Id,ct);
        // Retain previously mapped addresses if the GST/state selection changes.
        if(address is not null && (address.StateCode!=d.StateCode || !request.ProfileOnly && gst?.Gstin!=d.Gstin))
        {
            address.IsDefaultBilling=false; address.IsDefaultDispatch=false;
            await db.SaveChangesAsync(ct);
            address=null;
        }
        if(address is null)
        {
            address=new CompanyAddress {CompanyId=c.Id,IsDefaultBilling=true,IsDefaultDispatch=defaultDispatch};
            db.Add(address);
        }
        address.Lines=d.AddressLine1; address.Line2=d.AddressLine2; address.Locality=d.Locality;
        address.City=d.City; address.District=d.District; address.StateCode=d.StateCode; address.PostalCode=d.Pin; address.CountryCode=d.Country;
        await db.SaveChangesAsync(ct);
        if(!request.ProfileOnly && gst is not null && gst.Gstin!=d.Gstin)
        {
            gst.IsInvoiceDefault=false;
            gst.IsActive=false;
        }
        if(!request.ProfileOnly && d.Gstin is not null)
        {
            gst=await db.Set<GstRegistration>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id && x.Gstin==d.Gstin,ct);
            if(gst is null) { gst=new GstRegistration {CompanyId=c.Id,Gstin=d.Gstin,StateCode=d.StateCode}; db.Add(gst); }
            gst.IsActive=true;gst.StateCode=d.StateCode;
            if(!await db.Set<GstRegistrationAddress>().AnyAsync(x=>x.CompanyId==c.Id && x.GstRegistrationId==gst.Id && x.AddressId==address.Id,ct))
                db.Add(new GstRegistrationAddress {CompanyId=c.Id,GstRegistrationId=gst.Id,AddressId=address.Id});
        }
        foreach(var (purpose,kind,value) in new (string,string,string?)[] {
            ("Mobile","Phone",d.Mobile),("AlternateMobile","Phone",d.AlternateMobile),("Landline","Phone",d.Landline),
            ("Email","Email",d.Email),("Website","Other",d.Website),("ContactPerson","Other",d.ContactPerson)})
        {
            var contact=await db.Set<CompanyContact>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id && x.Purpose==purpose,ct);
            if(value is null) { if(contact is not null) db.Remove(contact); continue; }
            if(contact is null) {contact=new CompanyContact {CompanyId=c.Id,Purpose=purpose,Kind=kind};db.Add(contact);}
            contact.Value=value;
        }
        if(request.ProfileOnly) return;
        var bank=await db.Set<CompanyBankAccount>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id && x.IsDefault,ct);
        var ifsc=d.Ifsc ?? "";
        if(bank is not null && (bank.AccountNumber!=d.AccountNumber || bank.Ifsc!=ifsc))
        { bank.IsDefault=false; bank.IsActive=false; await db.SaveChangesAsync(ct); bank=null; }
        if(d.BankName is not null)
        {
            bank ??= await db.Set<CompanyBankAccount>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id && x.AccountNumber==d.AccountNumber && x.Ifsc==ifsc,ct);
            if(bank is null) {bank=new CompanyBankAccount {CompanyId=c.Id};db.Add(bank);}
            bank.BankName=d.BankName;bank.AccountHolder=d.AccountHolder!;bank.AccountNumber=d.AccountNumber!;bank.Ifsc=ifsc;
            bank.Branch=d.Branch;bank.AccountType=d.AccountType;bank.UpiId=d.UpiId;bank.IsActive=true;bank.IsDefault=true;
        }
        var settings=await db.Set<CompanySettings>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id,ct);
        if(settings is null) {settings=new CompanySettings {CompanyId=c.Id};db.Add(settings);}
        settings.InvoiceHeader=d.InvoiceHeader;settings.InvoiceFooter=d.InvoiceFooter;settings.SignatoryName=d.SignatoryName;
        settings.ShowBankDetails=d.BankName is not null;
        var terms=settings.TermsVersionId is null?null:await db.Set<TermsVersion>().SingleAsync(x=>x.Id==settings.TermsVersionId,ct);
        if((terms?.Text ?? "")!=(d.Terms ?? ""))
        {
            if(d.Terms is null) settings.TermsVersionId=null;
            else
            {
                var revision=(await db.Set<TermsVersion>().Where(x=>x.CompanyId==c.Id).MaxAsync(x=>(int?)x.Revision,ct) ?? 0)+1;
                var next=new TermsVersion {CompanyId=c.Id,Revision=revision,Text=d.Terms,ChangedByUserId=actor};
                db.Add(next);settings.TermsVersionId=next.Id;
            }
        }
        if(request.RemoveLogo) {settings.Logo1AssetId=null;settings.ShowPrimaryLogo=false;}
        if(request.Logo is not null)
        {
            var revision=(await db.Set<BrandingAsset>().Where(x=>x.CompanyId==c.Id && x.Kind=="Logo1").MaxAsync(x=>(int?)x.Revision,ct) ?? 0)+1;
            var logo=new BrandingAsset {CompanyId=c.Id,Kind="Logo1",Revision=revision,StorageId=Guid.NewGuid().ToString("N"),
                Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.Logo)),Content=request.Logo.ToArray(),
                ContentType="image/png",ChangedByUserId=actor};
            db.Add(logo);settings.Logo1AssetId=logo.Id;settings.ShowPrimaryLogo=true;
        }
    }

    private static async Task<CompanyDetails> Read(BillingDbContext db,Company c,Guid? current,bool includeLogo,CancellationToken ct,bool profileOnly=false)
    {
        var address=await Address(db,c.Id,ct);
        var gst=await db.Set<GstRegistration>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id&&x.IsInvoiceDefault&&x.IsActive,ct)
            ?? await Gst(db,c.Id,address?.Id,ct);
        var contacts=await db.Set<CompanyContact>().Where(x=>x.CompanyId==c.Id && x.Purpose!=null).ToDictionaryAsync(x=>x.Purpose!,x=>x.Value,ct);
        var bank=profileOnly?null:await db.Set<CompanyBankAccount>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id && x.IsDefault,ct);
        var settings=profileOnly?null:await db.Set<CompanySettings>().SingleOrDefaultAsync(x=>x.CompanyId==c.Id,ct);
        var terms=settings?.TermsVersionId is null?null:await db.Set<TermsVersion>().Where(x=>x.Id==settings.TermsVersionId).Select(x=>x.Text).SingleAsync(ct);
        var logo=includeLogo && settings?.Logo1AssetId is not null ?
            await db.Set<BrandingAsset>().Where(x=>x.Id==settings.Logo1AssetId).Select(x=>x.Content).SingleAsync(ct):null;
        var d=new CompanyDraft {Code=c.Code,LegalName=c.LegalName,TradeName=c.DisplayName,Tagline=c.Tagline,Iec=c.Iec,
            BusinessType=c.BusinessType,EstablishedOn=c.EstablishedOn?.ToString("yyyy-MM-dd"),AddressLine1=address?.Lines ?? "",
            AddressLine2=address?.Line2,Locality=address?.Locality,City=address?.City ?? "",District=address?.District,
            StateCode=address?.StateCode ?? "",Pin=address?.PostalCode ?? "",Country=address?.CountryCode ?? "IN",
            Mobile=contacts.GetValueOrDefault("Mobile"),AlternateMobile=contacts.GetValueOrDefault("AlternateMobile"),
            Landline=contacts.GetValueOrDefault("Landline"),Email=contacts.GetValueOrDefault("Email"),Website=contacts.GetValueOrDefault("Website"),
            ContactPerson=contacts.GetValueOrDefault("ContactPerson"),RegistrationType=c.RegistrationType,Gstin=gst?.Gstin,
            Pan=c.Pan,Tan=c.Tan,Cin=c.Cin,Udyam=c.Udyam,BankName=bank?.BankName,AccountHolder=bank?.AccountHolder,
            AccountNumber=bank?.AccountNumber,Ifsc=string.IsNullOrEmpty(bank?.Ifsc)?null:bank.Ifsc,Branch=bank?.Branch,AccountType=bank?.AccountType,UpiId=bank?.UpiId,
            InvoiceHeader=settings?.InvoiceHeader,InvoiceFooter=settings?.InvoiceFooter,SignatoryName=settings?.SignatoryName,Terms=terms};
        return new(c.Id,c.Version,c.IsActive,current==c.Id,d,c.CreatedUtc,c.CreatedByUserId,c.UpdatedUtc,c.UpdatedByUserId,logo);
    }
}

