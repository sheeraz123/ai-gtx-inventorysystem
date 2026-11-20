using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public enum CustomerOutcome {Success,InvalidInput,DuplicateWarning,SessionRequired,Forbidden,NoCompany,CompanyChanged,NotFound,Conflict,Ineligible,Unavailable}
public enum CustomerOperation {Context,List,Get,Create,Update,Status,AddAddress,UpdateAddress,DefaultBilling,DefaultShipping,AddContact,UpdateContact,PrimaryContact,Credit,Opening,InvoiceSelection,Snapshot,SaveInvoiceAddress}
public sealed record CustomerCompanyContext(Guid CompanyId,string Code,string Name);
public sealed record CustomerFilter(string? Search=null,string? Status=null,string? RegistrationType=null,int Page=1,int PageSize=50);
public sealed record CustomerSnapshotRequest(Guid? BillingAddressId=null,Guid? ShippingAddressId=null,Guid? ContactId=null,bool IncludePan=false,bool IncludeContactDetails=false);
public sealed record CustomerRequest(CustomerOperation Operation,Guid? ExpectedCompanyId=null,Guid? CustomerId=null,long? ExpectedVersion=null,
    CustomerDraft? Draft=null,CustomerFilter? Filter=null,string? Status=null,CustomerAddressDraft? Address=null,CustomerContactDraft? Contact=null,
    Guid? EntryId=null,CustomerCreditDraft? Credit=null,CustomerOpeningDraft? Opening=null,CustomerSnapshotRequest? Snapshot=null,bool AcknowledgeDuplicates=false);
public sealed record CustomerDuplicateWarning(Guid CustomerId,string Code,string Name,IReadOnlyList<string> Reasons);
public sealed record CustomerEligibility(bool CanSelectForNewInvoice,IReadOnlyList<string> MissingOrBlockingDetails,bool ReceivablesAvailable=false);
public sealed record CustomerCreditPolicy(bool CustomerActive,bool CreditHold,int CreditDays,decimal CreditLimit,string? PaymentTerms,
    bool PurchaseOrderRequired,bool OutstandingCheckAvailable=false,decimal? OutstandingBalance=null);
public sealed record CustomerListRow(Guid Id,long Version,string Code,string LegalName,string? TradeName,string? Gstin,string? Mobile,
    string? City,string? State,int CreditDays,decimal CreditLimit,string Status,bool CreditHold,string? Currency=null);
public sealed record CustomerPage(IReadOnlyList<CustomerListRow> Rows,int Total,int Page,int PageSize);
public sealed record CustomerDetail(Guid Id,long Version,Guid CompanyId,CustomerDraft Draft,Guid OpeningDraftId,long OpeningDraftVersion,
    DateTime CreatedUtc,string CreatedBy,DateTime UpdatedUtc,string UpdatedBy,CustomerEligibility Eligibility);
public sealed record CustomerSelection(Guid Id,long Version,string Code,string Name,string? TradeName,string? Gstin,string RegistrationType,
    Guid? DefaultBillingAddressId,Guid? DefaultShippingAddressId,CustomerCreditPolicy Credit,CustomerEligibility Eligibility);
public sealed record CustomerAddressSnapshot(Guid SourceAddressId,string RecipientName,string? Organization,string Line1,string? Line2,
    string? Landmark,string? Locality,string City,string? District,string? State,string? StateCode,string PostalCode,string Country,string? Mobile,string? Email);
public sealed record CustomerContactSnapshot(Guid SourceContactId,string Name,string? Designation,string? Mobile,string? Email);
public sealed record CustomerInvoiceSnapshot(int FormatVersion,DateTime CapturedUtc,Guid CompanyId,Guid CustomerId,long CustomerVersion,
    string Code,string LegalName,string? TradeName,string? DisplayName,string? Gstin,string? Pan,string RegistrationType,string? GstLegalName,string? RegisteredStateCode,
    string? SupplyClassification,DateOnly? GstEffectiveDate,CustomerAddressSnapshot BillTo,CustomerAddressSnapshot ShipTo,
    CustomerContactSnapshot? Contact,string? ContactNumber,string? ContactEmail,bool PurchaseOrderRequired,string? PaymentTerms,int CreditDays,string DeliveryMethod,string Currency);
public sealed record OpeningBalanceImportCandidate(Guid CompanyId,Guid CustomerId,Guid SourceDraftId,long SourceVersion,
    decimal AbsoluteAmount,string Nature,DateOnly? AsOfDate,string? Reference);
public sealed record CustomerFinancialAvailability(bool Available,decimal? OutstandingBalance,string Message);
public interface ICustomerFinancialSource
{
    Task<CustomerFinancialAvailability> GetAvailabilityAsync(Guid companyId,Guid customerId,CancellationToken ct);
}
public sealed record CustomerResult(CustomerOutcome Outcome,CustomerCompanyContext? Context=null,CustomerDetail? Detail=null,
    CustomerPage? Page=null,IReadOnlyDictionary<string,string>? Errors=null,IReadOnlyList<CustomerDuplicateWarning>? Warnings=null,
    IReadOnlyList<CustomerSelection>? Selection=null,CustomerInvoiceSnapshot? Snapshot=null,Guid? AddressId=null);
public interface ICustomerStore
{
    Task<CustomerResult> ExecuteAsync(CustomerRequest request,AuthenticatedIdentity actor,CancellationToken ct);
}

public sealed class CustomerService(AuthenticationService authentication,ICustomerStore store,ICustomerFinancialSource financial,IApplicationOperationCoordinator operations)
{
    public async Task<CustomerResult> ExecuteAsync(CustomerRequest request,CancellationToken ct=default)
    {
        CustomerResult? result=null;
        await operations.RunAsync(async token=>{result=await ExecuteCoreAsync(request,token);},ct);
        return result ?? new(CustomerOutcome.Unavailable);
    }
    private async Task<CustomerResult> ExecuteCoreAsync(CustomerRequest request,CancellationToken ct)
    {
        try
        {
            return await authentication.RunAuthenticatedAsync(async actor=>
            {
                if(request.Filter is { } f && (f.Page<1 || f.Page>1000000 || f.PageSize is <1 or >100 || f.Search?.Length>100 ||
                    !string.IsNullOrEmpty(f.Status)&&!CustomerRules.Statuses.Contains(f.Status) ||
                    !string.IsNullOrEmpty(f.RegistrationType)&&!CustomerRules.Registrations.Contains(f.RegistrationType)))
                    return new CustomerResult(CustomerOutcome.InvalidInput);
                if(request.Operation is CustomerOperation.Create or CustomerOperation.Update)
                {
                    if(request.Draft is null) return new CustomerResult(CustomerOutcome.InvalidInput);
                    var creating=request.Operation==CustomerOperation.Create;
                    var validated=CustomerRules.Validate(creating ? request.Draft with { Code="" } : request.Draft,
                        assignCodeOnSave:creating);
                    if(validated.Errors.Count>0) return new CustomerResult(CustomerOutcome.InvalidInput,Errors:validated.Errors);
                    request=request with {Draft=validated.Value};
                }
                if(request.Operation==CustomerOperation.Status&&!CustomerRules.Statuses.Contains(request.Status!))
                    return new CustomerResult(CustomerOutcome.InvalidInput);
                var result=await store.ExecuteAsync(request,actor,ct);
                if(result.Outcome==CustomerOutcome.SessionRequired) throw new UnauthorizedAccessException();
                return result;
            },ct);
        }
        catch(UnauthorizedAccessException) {return new(CustomerOutcome.SessionRequired);}
        catch(ArgumentException) {return new(CustomerOutcome.InvalidInput,Errors:new Dictionary<string,string>{{"General","Input contains invalid text or parameters."}});}
        catch(Exception e) when(e is System.Data.Common.DbException or IOException or InvalidOperationException)
        {return new(CustomerOutcome.Unavailable);}
    }
    public Task<CustomerResult> GetContextAsync(CancellationToken ct=default)=>ExecuteAsync(new(CustomerOperation.Context),ct);
    public Task<CustomerResult> CreateCustomerAsync(Guid company,CustomerDraft draft,bool acknowledgeDuplicates=false,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.Create,company,Draft:draft,AcknowledgeDuplicates:acknowledgeDuplicates),ct);
    public Task<CustomerResult> UpdateCustomerAsync(Guid company,Guid customer,long version,CustomerDraft draft,bool acknowledgeDuplicates=false,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.Update,company,customer,version,draft,AcknowledgeDuplicates:acknowledgeDuplicates),ct);
    public Task<CustomerResult> GetCustomerAsync(Guid company,Guid customer,CancellationToken ct=default)=>ExecuteAsync(new(CustomerOperation.Get,company,customer),ct);
    public Task<CustomerResult> ListCustomersAsync(Guid company,CustomerFilter filter,CancellationToken ct=default)=>ExecuteAsync(new(CustomerOperation.List,company,Filter:filter),ct);
    public Task<CustomerResult> SearchCustomersAsync(Guid company,CustomerFilter filter,CancellationToken ct=default)=>ListCustomersAsync(company,filter,ct);
    public Task<CustomerResult> ChangeCustomerStatusAsync(Guid company,Guid customer,long version,string status,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.Status,company,customer,version,Status:status),ct);
    public Task<CustomerResult> AddCustomerAddressAsync(Guid company,Guid customer,long version,CustomerAddressDraft address,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.AddAddress,company,customer,version,Address:address),ct);
    public Task<CustomerResult> UpdateCustomerAddressAsync(Guid company,Guid customer,long version,CustomerAddressDraft address,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.UpdateAddress,company,customer,version,Address:address),ct);
    public Task<CustomerResult> SetDefaultBillingAddressAsync(Guid company,Guid customer,long version,Guid address,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.DefaultBilling,company,customer,version,EntryId:address),ct);
    public Task<CustomerResult> SetDefaultShippingAddressAsync(Guid company,Guid customer,long version,Guid address,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.DefaultShipping,company,customer,version,EntryId:address),ct);
    public Task<CustomerResult> AddCustomerContactAsync(Guid company,Guid customer,long version,CustomerContactDraft contact,bool acknowledgeDuplicates=false,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.AddContact,company,customer,version,Contact:contact,AcknowledgeDuplicates:acknowledgeDuplicates),ct);
    public Task<CustomerResult> UpdateCustomerContactAsync(Guid company,Guid customer,long version,CustomerContactDraft contact,bool acknowledgeDuplicates=false,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.UpdateContact,company,customer,version,Contact:contact,AcknowledgeDuplicates:acknowledgeDuplicates),ct);
    public Task<CustomerResult> SetPrimaryContactAsync(Guid company,Guid customer,long version,Guid contact,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.PrimaryContact,company,customer,version,EntryId:contact),ct);
    public Task<CustomerResult> UpdateCustomerCreditPolicyAsync(Guid company,Guid customer,long version,CustomerCreditDraft credit,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.Credit,company,customer,version,Credit:credit),ct);
    public Task<CustomerResult> SaveCustomerOpeningBalanceDraftAsync(Guid company,Guid customer,long version,CustomerOpeningDraft opening,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.Opening,company,customer,version,Opening:opening),ct);
    public Task<CustomerResult> GetInvoiceEligibleCustomersAsync(Guid company,CustomerFilter filter,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.InvoiceSelection,company,Filter:filter),ct);
    public Task<CustomerResult> ResolveCustomerInvoiceSnapshotAsync(Guid company,Guid customer,CustomerSnapshotRequest selection,CancellationToken ct=default)=>
        ExecuteAsync(new(CustomerOperation.Snapshot,company,customer,Snapshot:selection),ct);
    public async Task<CustomerFinancialAvailability> GetFinancialAvailabilityAsync(Guid company,Guid customer,CancellationToken ct=default)
    {
        CustomerFinancialAvailability? result=null;
        await operations.RunAsync(async token=>{result=await FinancialCoreAsync(company,customer,token);},ct);
        return result ?? new(false,null,"Customer financial information is unavailable.");
    }
    private async Task<CustomerFinancialAvailability> FinancialCoreAsync(Guid company,Guid customer,CancellationToken ct)
    {
        try
        {
            return await authentication.RunAuthenticatedAsync(async actor=>
            {
                var result=await store.ExecuteAsync(new(CustomerOperation.Get,company,customer),actor,ct);
                if(result.Outcome==CustomerOutcome.SessionRequired) throw new UnauthorizedAccessException();
                if(result.Outcome!=CustomerOutcome.Success) return new CustomerFinancialAvailability(false,null,"Customer financial information is unavailable for the current company.");
                return await financial.GetAvailabilityAsync(company,customer,ct);
            },ct);
        }
        catch(UnauthorizedAccessException) {return new(false,null,"Sign in to access customer information.");}
        catch(Exception e) when(e is System.Data.Common.DbException or IOException or InvalidOperationException)
        {return new(false,null,"Customer financial information is unavailable.");}
    }
}

// Pure mutation composition; persistence and ownership checks stay at the transaction boundary.
public static class CustomerMutations
{
    public static CustomerDraft? Apply(CustomerRequest request,CustomerDraft current)=>request.Operation switch
    {
        CustomerOperation.Update=>request.Draft,
        CustomerOperation.Credit when request.Credit is not null=>current with {Credit=request.Credit},
        CustomerOperation.Opening when request.Opening is not null=>current with {Opening=request.Opening},
        CustomerOperation.AddAddress when request.Address is {Id:null}=>current with {Addresses=current.Addresses.Append(request.Address).ToArray()},
        CustomerOperation.UpdateAddress when request.Address?.Id is { } id && current.Addresses.Any(x=>x.Id==id)=>
            current with {Addresses=current.Addresses.Select(x=>x.Id==id?request.Address:x).ToArray()},
        CustomerOperation.DefaultBilling when current.Addresses.Any(x=>x.Id==request.EntryId)=>
            current with {Addresses=current.Addresses.Select(x=>x with {IsDefaultBilling=x.Id==request.EntryId}).ToArray()},
        CustomerOperation.DefaultShipping when current.Addresses.Any(x=>x.Id==request.EntryId)=>
            current with {Addresses=current.Addresses.Select(x=>x with {IsDefaultShipping=x.Id==request.EntryId}).ToArray()},
        CustomerOperation.AddContact when request.Contact is {Id:null}=>current with {Contacts=current.Contacts.Append(request.Contact).ToArray()},
        CustomerOperation.UpdateContact when request.Contact?.Id is { } id && current.Contacts.Any(x=>x.Id==id)=>
            current with {Contacts=current.Contacts.Select(x=>x.Id==id?request.Contact:x).ToArray()},
        CustomerOperation.PrimaryContact when current.Contacts.Any(x=>x.Id==request.EntryId)=>
            current with {Contacts=current.Contacts.Select(x=>x with {IsPrimary=x.Id==request.EntryId}).ToArray()},
        _=>null
    };
}
