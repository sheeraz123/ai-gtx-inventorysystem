using System.Reflection;
using System.Globalization;
using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class UnconfiguredCustomerFinancialSource : ICustomerFinancialSource
{
    public Task<CustomerFinancialAvailability> GetAvailabilityAsync(Guid companyId,Guid customerId,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new CustomerFinancialAvailability(false,null,"Available after invoicing/accounting implementation. No ledger or outstanding balance exists in this module."));
    }
}

public sealed class CustomerStore(DevelopmentDatabase database) : ICustomerStore
{
    public async Task<CustomerResult> ExecuteAsync(CustomerRequest request,AuthenticatedIdentity actor,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var installation=await db.Set<Installation>().SingleAsync(ct);
            if(installation.Id!=database.ExpectedInstallationId() || installation.DatabaseMode!="PlaintextDevelopment" || installation.SetupState!="Configured")
                return new(CustomerOutcome.Unavailable);
            var user=await db.Set<User>().SingleOrDefaultAsync(x=>x.Id==actor.UserId,ct);
            if(user?.IsActive!=true || user.Version!=actor.Version) return new(CustomerOutcome.SessionRequired);
            if(!await db.Set<SuperAdminAssignment>().AnyAsync(x=>x.UserId==actor.UserId,ct)) return new(CustomerOutcome.Forbidden);
            var selection=await db.Set<CompanySelection>().SingleOrDefaultAsync(ct);
            var company=selection is null?null:await db.Set<Company>().SingleOrDefaultAsync(x=>x.Id==selection.CompanyId&&x.IsActive,ct);
            if(company is null) return new(CustomerOutcome.NoCompany);
            var context=new CustomerCompanyContext(company.Id,company.Code,company.LegalName);
            if(request.Operation==CustomerOperation.Context) return new(CustomerOutcome.Success,context);
            if(request.ExpectedCompanyId!=company.Id) return new(CustomerOutcome.CompanyChanged,context);
            if(request.Operation is CustomerOperation.List or CustomerOperation.InvoiceSelection)
                return await List(db,request,context,ct);
            Customer customer;
            if(request.Operation==CustomerOperation.Create)
            {
                customer=new Customer {CompanyId=company.Id,CreatedByUserId=actor.UserId,UpdatedByUserId=actor.UserId};
            }
            else
            {
                var found=await db.Set<Customer>().SingleOrDefaultAsync(x=>x.CompanyId==company.Id&&x.Id==request.CustomerId,ct);
                if(found is null) return new(CustomerOutcome.NotFound,context);
                customer=found;
                if(request.Operation==CustomerOperation.Get) return new(CustomerOutcome.Success,context,await Read(db,customer,ct));
                if(request.Operation==CustomerOperation.Snapshot) return await Snapshot(db,customer,context,request.Snapshot ?? new(),ct);
                if(request.ExpectedVersion!=customer.Version) return new(CustomerOutcome.Conflict,context);
            }
            if(request.Operation==CustomerOperation.Status)
            {
                if(!CustomerRules.Statuses.Contains(request.Status!)) return new(CustomerOutcome.InvalidInput,context);
                customer.Status=request.Status!;
            }
            else
            {
                var proposed=request.Operation==CustomerOperation.Create?request.Draft:
                    CustomerMutations.Apply(request,(await Read(db,customer,ct)).Draft);
                if(proposed is null) return new(CustomerOutcome.InvalidInput,context);
                var creating=request.Operation==CustomerOperation.Create;
                var validated=CustomerRules.Validate(proposed with {Code=creating ? "" : customer.Code},assignCodeOnSave:creating);
                if(validated.Errors.Count>0) return new(CustomerOutcome.InvalidInput,context,Errors:validated.Errors);
                var draft=validated.Value;
                var addresses=await db.Set<CustomerAddress>().Where(x=>x.CompanyId==company.Id&&x.CustomerId==customer.Id).ToListAsync(ct);
                var contacts=await db.Set<CustomerContact>().Where(x=>x.CompanyId==company.Id&&x.CustomerId==customer.Id).ToListAsync(ct);
                if(draft.Addresses.Any(x=>x.Id is { } id&&!addresses.Any(a=>a.Id==id)) ||
                    draft.Contacts.Any(x=>x.Id is { } id&&!contacts.Any(c=>c.Id==id)))
                    return new(CustomerOutcome.InvalidInput,context,Errors:new Dictionary<string,string>{{"Ownership","An address or contact does not belong to this customer."}});
                if(draft.Addresses.Any(x=>x.Id is not null&&addresses.Single(a=>a.Id==x.Id).Version!=x.Version) ||
                    draft.Contacts.Any(x=>x.Id is not null&&contacts.Single(c=>c.Id==x.Id).Version!=x.Version))
                    return new(CustomerOutcome.Conflict,context);
                var identityChanged=request.Operation==CustomerOperation.Create || customer.NormalizedName!=CustomerRules.NormalizeName(draft.LegalName) ||
                    customer.NormalizedTradeName!=(string.IsNullOrWhiteSpace(draft.TradeName)?null:CustomerRules.NormalizeName(draft.TradeName)) ||
                    customer.Gstin!=draft.Gstin || customer.Pan!=draft.Pan || customer.NormalizedMobile!=CustomerRules.NormalizePhone(draft.Mobile) ||
                    draft.Contacts.Any(x=>x.Id is null&&CustomerRules.NormalizePhone(x.Mobile) is not null ||
                        x.Id is not null&&contacts.Single(c=>c.Id==x.Id).NormalizedMobile!=CustomerRules.NormalizePhone(x.Mobile));
                var warnings=identityChanged?await Duplicates(db,customer,draft,ct):[];
                if(warnings.Count>0 && !request.AcknowledgeDuplicates)
                    return new(CustomerOutcome.DuplicateWarning,context,Warnings:warnings);
                if(creating)
                {
                    var code=await NextCustomerCodeAsync(db,company.Id,company.Code,ct);
                    if(code is null) return new(CustomerOutcome.Conflict,context,Errors:new Dictionary<string,string>
                        {{"Code","No more automatic customer codes fit this company prefix."}});
                    draft=draft with {Code=code};
                    db.Add(customer);
                }
                Copy(draft,customer);Copy(draft.Credit,customer);
                customer.NormalizedName=CustomerRules.NormalizeName(draft.LegalName);
                customer.NormalizedTradeName=string.IsNullOrWhiteSpace(draft.TradeName)?null:CustomerRules.NormalizeName(draft.TradeName);
                customer.NormalizedMobile=CustomerRules.NormalizePhone(draft.Mobile);
                customer.UpdatedByUserId=actor.UserId;customer.UpdatedUtc=DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await WriteChildren(db,customer,draft,addresses,contacts,ct);
            }
            customer.UpdatedByUserId=actor.UserId;customer.UpdatedUtc=DateTime.UtcNow;
            db.Entry(customer).Property(x=>x.UpdatedUtc).IsModified=true;
            db.Add(new AuditEvent {CompanyId=company.Id,ActorUserId=actor.UserId,ActorKind="User",
                Action="Customer"+request.Operation,RecordType="Customer",RecordId=customer.Id.ToString(),
                ChangesJson=JsonSerializer.Serialize(new {Operation=request.Operation.ToString(),EntryId=request.EntryId})});
            await db.SaveChangesAsync(ct);
            var detail=await Read(db,customer,ct);
            ct.ThrowIfCancellationRequested();await transaction.CommitAsync(ct);
            return new(CustomerOutcome.Success,context,detail);
        }
        catch(OperationCanceledException) {throw;}
        catch(DbUpdateConcurrencyException) {return new(CustomerOutcome.Conflict);}
        catch(DbUpdateException e) when(e.InnerException is SqliteException {SqliteErrorCode:19}) {return new(CustomerOutcome.Conflict);}
        catch(SqliteException e) when(e.SqliteErrorCode==19) {return new(CustomerOutcome.Conflict);}
        catch(Exception e) when(e is SqliteException or DbUpdateException or IOException or InvalidOperationException or OverflowException)
        {return new(CustomerOutcome.Unavailable);}
    }

    private static async Task<string?> NextCustomerCodeAsync(BillingDbContext db,Guid companyId,string companyCode,CancellationToken ct)
    {
        var prefix=companyCode+"CUST";
        long highest=0;
        var codes=db.Set<Customer>().Where(x=>x.CompanyId==companyId&&x.Code.StartsWith(prefix)).Select(x=>x.Code);
        await foreach(var code in codes.AsAsyncEnumerable().WithCancellation(ct))
        {
            if(long.TryParse(code.AsSpan(prefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out var number))
                highest=Math.Max(highest,number);
        }
        // Allocation and insert share the SQLite write transaction. The unique index is the final guard.
        if(highest==long.MaxValue) return null;
        var next=prefix+(highest+1).ToString("D3",CultureInfo.InvariantCulture);
        return next.Length<=40?next:null;
    }

    private static readonly HashSet<string> Protected=new(StringComparer.Ordinal)
        {"Id","Version","CompanyId","CustomerId","CreatedUtc","UpdatedUtc","CreatedByUserId","UpdatedByUserId","NormalizedName","NormalizedTradeName","NormalizedMobile"};
    private static void Copy(object source,object target)
    {
        foreach(var property in source.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
        {
            if(Protected.Contains(property.Name)) continue;
            var destination=target.GetType().GetProperty(property.Name);
            if(destination?.CanWrite==true && destination.PropertyType==property.PropertyType)
                destination.SetValue(target,property.GetValue(source));
        }
    }
    private static async Task<List<CustomerDuplicateWarning>> Duplicates(BillingDbContext db,Customer customer,CustomerDraft draft,CancellationToken ct)
    {
        var names=new[]{CustomerRules.NormalizeName(draft.LegalName),CustomerRules.NormalizeName(draft.TradeName)}.Where(x=>x.Length>0).ToArray();
        var phones=draft.Contacts.Select(x=>CustomerRules.NormalizePhone(x.Mobile)).Append(CustomerRules.NormalizePhone(draft.Mobile))
            .Where(x=>!string.IsNullOrEmpty(x)).Select(x=>x!).Distinct().ToArray();
        var query=db.Set<Customer>().AsNoTracking().Where(x=>x.CompanyId==customer.CompanyId&&x.Id!=customer.Id&&
            (names.Contains(x.NormalizedName)||x.NormalizedTradeName!=null&&names.Contains(x.NormalizedTradeName)||
             draft.Gstin!=null&&x.Gstin==draft.Gstin||draft.Pan!=null&&x.Pan==draft.Pan||
             x.NormalizedMobile!=null&&phones.Contains(x.NormalizedMobile)||
             db.Set<CustomerContact>().Any(c=>c.CompanyId==customer.CompanyId&&c.CustomerId==x.Id&&c.NormalizedMobile!=null&&phones.Contains(c.NormalizedMobile))));
        var matches=await query.OrderBy(x=>x.Code).Take(20).ToListAsync(ct);
        var contactPhoneMatches=await db.Set<CustomerContact>().Where(x=>x.CompanyId==customer.CompanyId&&x.NormalizedMobile!=null&&phones.Contains(x.NormalizedMobile))
            .Select(x=>x.CustomerId).Distinct().ToListAsync(ct);
        return matches.Select(x=>new CustomerDuplicateWarning(x.Id,x.Code,x.LegalName,
            new[]{"Name","GSTIN","PAN","Mobile"}.Where(reason=>reason switch {
                "Name"=>names.Contains(x.NormalizedName)||x.NormalizedTradeName!=null&&names.Contains(x.NormalizedTradeName),
                "GSTIN"=>draft.Gstin!=null&&draft.Gstin==x.Gstin,"PAN"=>draft.Pan!=null&&draft.Pan==x.Pan,
                _=>x.NormalizedMobile!=null&&phones.Contains(x.NormalizedMobile)||contactPhoneMatches.Contains(x.Id)
            }).ToArray())).ToList();
    }

    private static async Task WriteChildren(BillingDbContext db,Customer customer,CustomerDraft d,List<CustomerAddress> addresses,List<CustomerContact> contacts,CancellationToken ct)
    {
        var bill=d.Addresses.SingleOrDefault(x=>x.IsDefaultBilling);
        var ship=d.Addresses.SingleOrDefault(x=>x.IsDefaultShipping);
        var primary=d.Contacts.SingleOrDefault(x=>x.IsPrimary);
        // Clear displaced unique flags before assigning replacements; all phases share the same transaction.
        foreach(var a in addresses)
        {
            if(a.IsDefaultBilling&&a.Id!=bill?.Id) a.IsDefaultBilling=false;
            if(a.IsDefaultShipping&&a.Id!=ship?.Id) a.IsDefaultShipping=false;
        }
        foreach(var c in contacts) if(c.IsPrimary&&c.Id!=primary?.Id) c.IsPrimary=false;
        await db.SaveChangesAsync(ct);
        foreach(var old in addresses.Where(x=>!d.Addresses.Any(a=>a.Id==x.Id)))
        {old.IsActive=false;old.IsDefaultBilling=false;old.IsDefaultShipping=false;}
        foreach(var old in contacts.Where(x=>!d.Contacts.Any(c=>c.Id==x.Id))) {old.IsActive=false;old.IsPrimary=false;}
        foreach(var item in d.Addresses)
        {
            var row=item.Id is null?new CustomerAddress {CompanyId=customer.CompanyId,CustomerId=customer.Id}:addresses.Single(x=>x.Id==item.Id);
            if(item.Id is null) db.Add(row);Copy(item,row);
        }
        foreach(var item in d.Contacts)
        {
            var row=item.Id is null?new CustomerContact {CompanyId=customer.CompanyId,CustomerId=customer.Id}:contacts.Single(x=>x.Id==item.Id);
            if(item.Id is null) db.Add(row);Copy(item,row);row.NormalizedMobile=CustomerRules.NormalizePhone(item.Mobile);
        }
        var opening=await db.Set<CustomerOpeningBalanceDraft>().SingleOrDefaultAsync(x=>x.CompanyId==customer.CompanyId&&x.CustomerId==customer.Id,ct);
        if(opening is null) {opening=new CustomerOpeningBalanceDraft {CompanyId=customer.CompanyId,CustomerId=customer.Id};db.Add(opening);}
        Copy(d.Opening,opening);
        var bank=await db.Set<CustomerBankDetail>().SingleOrDefaultAsync(x=>x.CompanyId==customer.CompanyId&&x.CustomerId==customer.Id,ct);
        var hasBank=d.Bank is not null&&typeof(CustomerBankDraft).GetProperties().Any(p=>!string.IsNullOrWhiteSpace((string?)p.GetValue(d.Bank)));
        if(hasBank)
        {
            if(bank is null){bank=new CustomerBankDetail {CompanyId=customer.CompanyId,CustomerId=customer.Id};db.Add(bank);}
            Copy(d.Bank!,bank);bank.IsActive=true;
        }
        else if(bank is not null) bank.IsActive=false;
    }

    private static async Task<CustomerDetail> Read(BillingDbContext db,Customer c,CancellationToken ct)
    {
        var addresses=await db.Set<CustomerAddress>().AsNoTracking().Where(x=>x.CompanyId==c.CompanyId&&x.CustomerId==c.Id).OrderBy(x=>x.Id).ToListAsync(ct);
        var contacts=await db.Set<CustomerContact>().AsNoTracking().Where(x=>x.CompanyId==c.CompanyId&&x.CustomerId==c.Id).OrderBy(x=>x.Id).ToListAsync(ct);
        var opening=await db.Set<CustomerOpeningBalanceDraft>().AsNoTracking().SingleAsync(x=>x.CompanyId==c.CompanyId&&x.CustomerId==c.Id,ct);
        var bank=await db.Set<CustomerBankDetail>().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==c.CompanyId&&x.CustomerId==c.Id&&x.IsActive,ct);
        var draft=new CustomerDraft();Copy(c,draft);
        var credit=new CustomerCreditDraft();Copy(c,credit);
        var open=new CustomerOpeningDraft();Copy(opening,open);
        CustomerBankDraft? bankDraft=null;if(bank is not null){bankDraft=new();Copy(bank,bankDraft);}
        draft=draft with {Credit=credit,Opening=open,Bank=bankDraft,
            Addresses=addresses.Select(x=>{var a=new CustomerAddressDraft();Copy(x,a);return a with {Id=x.Id,Version=x.Version};}).ToArray(),
            Contacts=contacts.Select(x=>{var contact=new CustomerContactDraft();Copy(x,contact);return contact with {Id=x.Id,Version=x.Version};}).ToArray()};
        var authors=await db.Set<User>().Where(x=>x.Id==c.CreatedByUserId||x.Id==c.UpdatedByUserId).Select(x=>new{x.Id,x.Username}).ToDictionaryAsync(x=>x.Id,x=>x.Username,ct);
        return new(c.Id,c.Version,c.CompanyId,draft,opening.Id,opening.Version,c.CreatedUtc,authors.GetValueOrDefault(c.CreatedByUserId,"Unavailable"),
            c.UpdatedUtc,authors.GetValueOrDefault(c.UpdatedByUserId,"Unavailable"),Eligibility(draft,null));
    }

    private static CustomerEligibility Eligibility(CustomerDraft d,CustomerSnapshotRequest? selected)
    {
        var missing=new List<string>();
        if(d.Status!="Active") missing.Add("Customer is "+d.Status+".");
        if(d.Credit.CreditHold) missing.Add("Customer is on credit hold; release the hold before new invoice selection.");
        if(string.IsNullOrWhiteSpace(d.LegalName)||string.IsNullOrWhiteSpace(d.Code)) missing.Add("Customer identity is incomplete.");
        var bill=ResolveAddress(d,selected?.BillingAddressId,true);var ship=ResolveAddress(d,selected?.ShippingAddressId,false);
        if(bill is null) missing.Add("Select an active Billing/Both address or configure a default billing address.");
        if(ship is null) missing.Add("Select an active Shipping/Both address or configure a default shipping address.");
        var tax=CustomerRules.Validate(d).Errors.Where(x=>x.Key is "Gstin" or "Pan" or "GstStateCode" or "RegistrationType").Select(x=>x.Value);
        missing.AddRange(tax);
        return new(missing.Count==0,missing);
    }
    private static CustomerAddressDraft? ResolveAddress(CustomerDraft d,Guid? id,bool billing)=>
        d.Addresses.SingleOrDefault(a=>a.IsActive && (billing?a.AddressType is "Billing" or "Both":a.AddressType is "Shipping" or "Both") &&
            (id is not null?a.Id==id:billing?a.IsDefaultBilling:a.IsDefaultShipping));
    private static async Task<CustomerResult> Snapshot(BillingDbContext db,Customer customer,CustomerCompanyContext context,CustomerSnapshotRequest request,CancellationToken ct)
    {
        var detail=await Read(db,customer,ct);var d=detail.Draft;
        var eligibility=Eligibility(d,request);
        if(!eligibility.CanSelectForNewInvoice) return new(CustomerOutcome.Ineligible,context,detail with {Eligibility=eligibility});
        var bill=ResolveAddress(d,request.BillingAddressId,true)!;var ship=ResolveAddress(d,request.ShippingAddressId,false)!;
        var contact=request.ContactId is not null?d.Contacts.SingleOrDefault(x=>x.Id==request.ContactId&&x.IsActive):
            request.IncludeContactDetails?d.Contacts.SingleOrDefault(x=>x.IsPrimary&&x.IsActive):null;
        if(request.ContactId is not null&&contact is null) return new(CustomerOutcome.InvalidInput,context);
        CustomerAddressSnapshot Address(CustomerAddressDraft a)=>new(a.Id!.Value,a.RecipientName,a.Organization,a.Line1,a.Line2,a.Landmark,a.Locality,
            a.City,a.District,a.StateName,a.StateCode,a.PostalCode,a.Country,request.IncludeContactDetails?a.Mobile:null,request.IncludeContactDetails?a.Email:null);
        var snapshot=new CustomerInvoiceSnapshot(1,DateTime.UtcNow,customer.CompanyId,customer.Id,customer.Version,d.Code,d.LegalName,
            d.TradeName,d.DisplayName,d.Gstin,request.IncludePan?d.Pan:null,d.RegistrationType,d.GstLegalName,d.GstStateCode,d.SupplyClassification,d.GstEffectiveDate,
            Address(bill),Address(ship),contact is not null?new(contact.Id!.Value,contact.Name,contact.Designation,request.IncludeContactDetails?contact.Mobile:null,request.IncludeContactDetails?contact.Email:null):null,
            request.IncludeContactDetails?contact?.Mobile??bill.Mobile??d.Mobile:null,request.IncludeContactDetails?contact?.Email??bill.Email??d.Email:null,
            d.Credit.PurchaseOrderRequired,d.Credit.PaymentTerms,d.Credit.CreditDays,d.Credit.DeliveryMethod,d.Credit.Currency);
        return new(CustomerOutcome.Success,context,Snapshot:snapshot);
    }

    private static async Task<CustomerResult> List(BillingDbContext db,CustomerRequest request,CustomerCompanyContext context,CancellationToken ct)
    {
        var filter=request.Filter ?? new();var query=db.Set<Customer>().AsNoTracking().Where(x=>x.CompanyId==context.CompanyId);
        if(!string.IsNullOrEmpty(filter.Status)) query=query.Where(x=>x.Status==filter.Status);
        if(!string.IsNullOrEmpty(filter.RegistrationType)) query=query.Where(x=>x.RegistrationType==filter.RegistrationType);
        if(!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text=filter.Search.Trim();var normalized=CustomerRules.NormalizeName(text);
            var phone=text.All(x=>char.IsAsciiDigit(x)||x is '+' or '(' or ')' or ' ' or '.' or '-')?CustomerRules.NormalizePhone(text):null;
            query=query.Where(x=>x.Code.Contains(normalized)||x.NormalizedName.Contains(normalized)||
                x.NormalizedTradeName!=null&&x.NormalizedTradeName.Contains(normalized)||x.Gstin!=null&&x.Gstin.Contains(normalized)||
                db.Set<CustomerAddress>().Any(a=>a.CompanyId==context.CompanyId&&a.CustomerId==x.Id&&a.IsActive&&a.City.ToUpper().Contains(normalized))||
                phone!=null&&x.NormalizedMobile!=null&&x.NormalizedMobile.Contains(phone)||
                phone!=null&&db.Set<CustomerContact>().Any(c=>c.CompanyId==context.CompanyId&&c.CustomerId==x.Id&&c.IsActive&&c.NormalizedMobile!=null&&c.NormalizedMobile.Contains(phone)));
        }
        if(request.Operation==CustomerOperation.InvoiceSelection)
            query=query.Where(x=>x.Status=="Active"&&!x.CreditHold&&
                db.Set<CustomerAddress>().Any(a=>a.CompanyId==context.CompanyId&&a.CustomerId==x.Id&&a.IsActive&&a.IsDefaultBilling)&&
                db.Set<CustomerAddress>().Any(a=>a.CompanyId==context.CompanyId&&a.CustomerId==x.Id&&a.IsActive&&a.IsDefaultShipping));
        var total=await query.CountAsync(ct);
        var rows=await query.OrderBy(x=>x.Code).Skip((filter.Page-1)*filter.PageSize).Take(filter.PageSize).ToListAsync(ct);
        var output=new List<CustomerListRow>();var selections=new List<CustomerSelection>();
        foreach(var c in rows)
        {
            var address=await db.Set<CustomerAddress>().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==context.CompanyId&&x.CustomerId==c.Id&&x.IsDefaultBilling&&x.IsActive,ct);
            output.Add(new(c.Id,c.Version,c.Code,c.LegalName,c.TradeName,c.Gstin,c.Mobile,address?.City,address?.StateName,c.CreditDays,c.CreditLimit,c.Status,c.CreditHold));
            if(request.Operation==CustomerOperation.InvoiceSelection)
            {
                var detail=await Read(db,c,ct);var d=detail.Draft;
                selections.Add(new(c.Id,c.Version,c.Code,c.LegalName,c.TradeName,c.Gstin,c.RegistrationType,
                    d.Addresses.SingleOrDefault(a=>a.IsDefaultBilling&&a.IsActive)?.Id,d.Addresses.SingleOrDefault(a=>a.IsDefaultShipping&&a.IsActive)?.Id,
                    new(c.Status=="Active",c.CreditHold,c.CreditDays,c.CreditLimit,c.PaymentTerms,c.PurchaseOrderRequired),detail.Eligibility));
            }
        }
        return new(CustomerOutcome.Success,context,Page:new(output,total,filter.Page,filter.PageSize),Selection:selections);
    }
}

