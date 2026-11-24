using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class EnvelopePrintingStore(DevelopmentDatabase database) : IEnvelopePrintingStore
{
    public async Task<EnvelopeCustomers> LookupAsync(AuthenticatedIdentity actor,Guid company,string query,Guid? customer,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            var access=await CheckAccessAsync(db,actor,company,ct);
            if(access.Outcome!=CompanyOutcome.Success) return new(access.Outcome);
            var customers=db.Set<Customer>().AsNoTracking().Where(x=>x.CompanyId==company);
            if(customer is Guid id)
            {
                var match=await customers.Where(x=>x.Id==id).Select(x=>new {x.LegalName,x.Mobile}).SingleOrDefaultAsync(ct);
                if(match is null) return new(CompanyOutcome.NotFound);
                var addresses=await db.Set<CustomerAddress>().AsNoTracking().Where(x=>x.CompanyId==company && x.CustomerId==id &&
                    x.IsActive && (x.AddressType=="Shipping" || x.AddressType=="Both")).OrderBy(x=>x.Label)
                    .Select(x=>new CustomerAddressDraft {Id=x.Id,Label=x.Label,Line1=x.Line1,Line2=x.Line2,Landmark=x.Landmark,
                        Locality=x.Locality,City=x.City,StateName=x.StateName,StateCode=x.StateCode,PostalCode=x.PostalCode,
                        Mobile=x.Mobile,IsDefaultShipping=x.IsDefaultShipping}).ToArrayAsync(ct);
                return new(CompanyOutcome.Success,Recipient:new(match.LegalName,match.Mobile,addresses));
            }
            var term=CustomerRules.NormalizeName(query);
            if(term.Length is <3 or >100) return new(CompanyOutcome.Success,Matches:[]);
            var matches=await customers.Where(x=>x.Code.Contains(term) || x.NormalizedName.Contains(term))
                .OrderBy(x=>x.LegalName).ThenBy(x=>x.Code).Take(50)
                .Select(x=>new EnvelopeCustomer(x.Id,x.Code,x.LegalName)).ToArrayAsync(ct);
            return new(CompanyOutcome.Success,Matches:matches);
        }
        catch(Exception error) when(error is SqliteException or DbUpdateException or IOException or InvalidOperationException)
        {return new(CompanyOutcome.Unavailable);}
    }

    private async Task<(CompanyOutcome Outcome,bool Admin)> CheckAccessAsync(BillingDbContext db,AuthenticatedIdentity actor,
        Guid company,CancellationToken ct)
    {
        var session=await ScopedAccess.VerifyAsync(db,database.ExpectedInstallationId(),actor,ct);
        if(session!=CompanyOutcome.Success) return (session,false);
        if(!await db.Set<Company>().AnyAsync(x=>x.Id==company && x.IsActive &&
            db.Set<CompanySelection>().Any(selected=>selected.CompanyId==x.Id),ct)) return (CompanyOutcome.Conflict,false);
        if(await ScopedAccess.IsSuperAdminAsync(db,actor.UserId,ct)) return (CompanyOutcome.Success,true);
        var grants=await ScopedAccess.ResolveAsync(db,actor.UserId,company,ct);
        return (grants.Contains("Customer.View") && grants.Contains("EnvelopePrinting.Print")
            ?CompanyOutcome.Success:CompanyOutcome.Forbidden,false);
    }

    public async Task<CompanyOutcome> AuthorizeAsync(AuthenticatedIdentity actor,Guid company,Guid customer,EnvelopeMedia media,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var access=await CheckAccessAsync(db,actor,company,ct);
            if(access.Outcome!=CompanyOutcome.Success) return access.Outcome;
            if(customer!=Guid.Empty && !await db.Set<Customer>().AnyAsync(x=>x.CompanyId==company && x.Id==customer,ct))
                return CompanyOutcome.NotFound;
            if(customer==Guid.Empty && !access.Admin) return CompanyOutcome.Forbidden;
            if(!EnvelopeConfiguration.Calibrated(media) || customer!=Guid.Empty &&
                (await SettingsStore.ReadCompanyAsync(db,company,ct)).Get(EnvelopeConfiguration.Media)!=media)
                return CompanyOutcome.Conflict;
            db.Add(new AuditEvent {CompanyId=company,ActorUserId=actor.UserId,ActorKind="User",
                Action=customer==Guid.Empty?"EnvelopeCalibrationPrintAuthorized":"EnvelopePrintAuthorized",
                RecordType=customer==Guid.Empty?"Company":"Customer",
                RecordId=(customer==Guid.Empty?company:customer).ToString(),ChangesJson="{}"});
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return CompanyOutcome.Success;
        }
        catch(Exception error) when(error is SqliteException or DbUpdateException or IOException or InvalidOperationException)
        {return CompanyOutcome.Unavailable;}
    }
}
