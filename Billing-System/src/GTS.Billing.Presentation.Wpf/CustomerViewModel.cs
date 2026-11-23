using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

public sealed class CustomerViewModel : ObservableObject,IDisposable
{
    private readonly CustomerService service;
    private readonly ApplicationLifetime lifetime;
    private CancellationTokenSource viewCancellation=new();
    private bool loading;
    private bool changingDefaults;
    private bool disposed;
    private CustomerDetail? editing;
    public ObservableCollection<CustomerListRow> Customers {get;}=[];
    public ObservableCollection<CustomerEntryEditor> Addresses {get;}=[];
    public ObservableCollection<CustomerEntryEditor> Contacts {get;}=[];
    public IEnumerable<CustomerEntryEditor> BillingAddresses=>Addresses.Where(x=>x.Field("AddressType").Text is "Billing" or "Both").ToArray();
    public IEnumerable<CustomerEntryEditor> ShippingAddresses=>Addresses.Where(x=>x.Field("AddressType").Text is "Shipping" or "Both").ToArray();
    public IReadOnlyList<CustomerField> GeneralFields {get;}
    public IReadOnlyList<CustomerField> TaxFields {get;}
    public IReadOnlyList<CustomerField> CreditFields {get;}
    public IReadOnlyList<CustomerField> OpeningFields {get;}
    public IReadOnlyList<CustomerField> BankFields {get;}
    public IReadOnlyList<CustomerField> NotesFields {get;}
    public IReadOnlyList<CustomerChoice> StatusFilters {get;}=new[]{new CustomerChoice("","All statuses")}.Concat(CustomerRules.Statuses.Select(x=>new CustomerChoice(x,x))).ToArray();
    public IReadOnlyList<CustomerChoice> RegistrationFilters {get;}=new[]{new CustomerChoice("","All registrations")}.Concat(CustomerRules.Registrations.Select(x=>new CustomerChoice(x,CustomerEditorFields.ChoiceLabel(x)))).ToArray();
    private IEnumerable<(string Prefix,IReadOnlyList<CustomerField> Fields)> Groups=>new[]{
        ("",GeneralFields),("",TaxFields),("Credit.",CreditFields),("Opening.",OpeningFields),("Bank.",BankFields),("",NotesFields)};
    public event Action? SessionExpired;
    public Func<IReadOnlyList<CustomerDuplicateWarning>,bool>? ConfirmDuplicates {get;set;}
    public Func<bool>? ConfirmShippingReplacement {get;set;}
    public bool GraftexLayout=>ConfigurationModule.GraftexCustomer(SettingsDisplay.Current);
    public IReadOnlyList<CustomerField> GraftexGeneralLeft=>GeneralFields
        .Where(item=>item.Name is not ("Mobile" or "Email" or "Status")).ToArray();
    public IEnumerable<CustomerField> GraftexGeneralRight=>GeneralFields.Where(item=>item.Name is "Mobile" or "Email" or "Status");
    public IEnumerable<CustomerField> GraftexBankLeft=>BankFields.Where(item=>item.Name is "BankName" or "AccountNumber" or "AccountHolder");
    public IEnumerable<CustomerField> GraftexBankRight=>BankFields.Where(item=>item.Name is not ("BankName" or "AccountNumber" or "AccountHolder"));
    public bool StandardBilling=>!GraftexLayout && ShowTab("Addresses");
    public bool CombinedAddresses=>GraftexLayout && ShowTab("Addresses");
    public bool ShowTax=>ShowTab("Tax");
    public bool ShowBank=>CanSeeBank && ShowTab("Bank");
    public bool ShowContacts=>ShowTab("Contacts");
    public bool ShowCredit=>ShowTab("Credit");
    public bool ShowOpening=>ShowTab("Opening");
    public bool ShowNotes=>ShowTab("Notes");
    private bool ShowTab(string name)=>ConfigurationModule.CustomerTabVisible(SettingsDisplay.Current,name);
    public IRelayCommand CopyBillingCommand {get;}
    public CustomerEntryEditor? CopySource {get;set;}
    public CustomerEntryEditor? CopyDestination {get;set;}
    private CustomerCompanyContext? company;
    public string CompanyCaption=>company is null?"No company selected — select an active company in Company Master.":"Customers for "+company.Name+" ("+company.Code+")";
    private string message="";
    public string Message {get=>message;private set=>SetProperty(ref message,value);}
    private string errors="";
    public string ValidationSummary {get=>errors;private set=>SetProperty(ref errors,value);}
    private string audit="";
    public string AuditCaption {get=>audit;private set=>SetProperty(ref audit,value);}
    private string readiness="";
    public string InvoiceReadiness {get=>readiness;private set=>SetProperty(ref readiness,value);}
    public string FinancialAvailability=>"Available after invoicing/accounting implementation. Ledger, invoice/receipt history, outstanding, ageing, credit/debit notes and statements are unavailable.";
    public string OpeningExplanation=>"DRAFT ONLY — Debit: customer owes the business; Credit: business owes the customer. This is not a posted ledger entry or an outstanding total.";
    public string CreditWarning=>CreditFields.Single(x=>x.Name=="CreditHold").Checked?
        (SettingsDisplay.Current.Get(SettingsRegistry.CreditWarning)?"CREDIT HOLD — customer is not offered for new invoice selection. No live receivables check is available.":"CREDIT HOLD"):
        "Stored credit policy only — enforcement against live receivables is unavailable.";
    private bool busy;
    public bool Busy {get=>busy;private set {SetProperty(ref busy,value);NotifyState();}}
    private bool editorOpen;
    public bool EditorOpen {get=>editorOpen;private set {SetProperty(ref editorOpen,value);NotifyState();}}
    private bool viewOnly;
    public bool ViewOnly {get=>viewOnly;private set {SetProperty(ref viewOnly,value);NotifyState();}}
    private AccessSnapshot access=AccessSnapshot.Denied;
    private bool Can(string action)=>access.Allows("Customer."+action);
    public bool CanEdit=>EditorOpen&&!ViewOnly&&!Busy&&!lifetime.IsStopping&&!disposed && Can(editing is null?"Create":"Edit");
    public bool CanEditCredit=>CanEdit && Can("CreditEdit");
    public bool CanEditBank=>CanEdit && Can("BankView") && Can("BankEdit");
    public bool CanSeeBank=>Can("BankView");
    public void SetAccess(AccessSnapshot value)
    {
        if (access.EquivalentTo(value)) return;
        access=value;
        if (!Can("View") || EditorOpen) Clear();
        NotifyState();
    }
    public bool CanCancel=>!Busy&&!lifetime.IsStopping;
    private bool dirty;
    public bool Dirty {get=>dirty;private set=>SetProperty(ref dirty,value);}
    private CustomerListRow? selected;
    public CustomerListRow? Selected {get=>selected;set {SetProperty(ref selected,value);NotifyState();}}
    private string search="";
    private readonly MasterSearch liveSearch = new();
    public string Search
    {
        get => search;
        set
        {
            var previous = MasterSearch.Query(search);
            if (!SetProperty(ref search,value)) return;
            var query = MasterSearch.Query(value);
            if (query != previous) QueueSearch(query.Length > 0);
        }
    }
    private string statusFilter="";
    public string StatusFilter {get=>statusFilter;set {if(SetProperty(ref statusFilter,value)) QueueSearch(false);}}
    private string registrationFilter="";
    public string RegistrationFilter {get=>registrationFilter;set {if(SetProperty(ref registrationFilter,value)) QueueSearch(false);}}
    private int page=1;
    private int total;
    public string PageCaption=>$"Page {page} · {total} matching customers";
    public IAsyncRelayCommand RefreshCommand {get;}
    public IRelayCommand AddCommand {get;}
    public IAsyncRelayCommand EditCommand {get;}
    public IAsyncRelayCommand ViewCommand {get;}
    public IAsyncRelayCommand EditViewedCommand {get;}
    public IAsyncRelayCommand<string> StatusCommand {get;}
    public IAsyncRelayCommand SaveCommand {get;}
    public IAsyncRelayCommand PreviousPageCommand {get;}
    public IAsyncRelayCommand NextPageCommand {get;}
    public IRelayCommand AddBillingCommand {get;}
    public IRelayCommand AddShippingCommand {get;}
    public IRelayCommand AddContactCommand {get;}
    private bool CanBrowse=>!Busy&&!EditorOpen&&!lifetime.IsStopping&&!disposed;

    public CustomerViewModel(CustomerService service,ApplicationLifetime lifetime)
    {
        this.service=service;this.lifetime=lifetime;
        GeneralFields=[
            new CustomerField("Code","Customer code") { ReadOnly=true },CustomerEditorFields.Text("LegalName","Customer / legal name *"),
            CustomerEditorFields.Text("TradeName","Trade name"),CustomerEditorFields.Text("DisplayName","Display name"),
            CustomerEditorFields.Choice("CustomerType","Customer type *",CustomerRules.Types),
            CustomerEditorFields.Text("GroupLabel","Customer group (optional label)"),CustomerEditorFields.Text("CategoryLabel","Customer classification (not Product Category)"),
            CustomerEditorFields.Choice("Status","Status *",CustomerRules.Statuses),CustomerEditorFields.Date("SinceDate","Customer since"),
            CustomerEditorFields.Text("Mobile","General mobile"),CustomerEditorFields.Text("Email","General email")];
        foreach(var field in GeneralFields)
            field.UseCustomerConfiguration=ConfigurationModule.CustomerFieldCatalog.Any(item=>item.Key==field.Name);
        TaxFields=[CustomerEditorFields.Choice("RegistrationType","GST registration type *",CustomerRules.Registrations),
            CustomerEditorFields.Text("Gstin","GSTIN"),CustomerEditorFields.Text("Pan","PAN"),CustomerEditorFields.Text("GstLegalName","Legal name as per GST"),
            CustomerEditorFields.State("GstStateCode","GST registered state / UT",true),CustomerEditorFields.Date("GstEffectiveDate","Registration effective date"),
            CustomerEditorFields.Text("SupplyClassification","Supply / tax treatment label (no rates inferred)"),
            CustomerEditorFields.Long("ExemptionNotes","Exemption / special-status notes")];
        CreditFields=[CustomerEditorFields.Text("PaymentTerms","Default payment terms"),CustomerEditorFields.Integer("CreditDays","Credit days *"),
            CustomerEditorFields.Money("CreditLimit","Credit limit * (not outstanding)"),CustomerEditorFields.Check("CreditHold","Credit hold / block credit"),
            CustomerEditorFields.Text("PaymentMode","Preferred payment mode"),CustomerEditorFields.Choice("DeliveryMethod","Invoice delivery preference",CustomerRules.DeliveryMethods),
            CustomerEditorFields.Choice("Currency","Preferred currency *",CustomerRules.Currencies),CustomerEditorFields.Check("PurchaseOrderRequired","Customer PO required"),
            CustomerEditorFields.Long("BillingInstructions","Billing instructions")];
        OpeningFields=[CustomerEditorFields.Money("Amount","Opening amount (absolute draft amount)"),CustomerEditorFields.Choice("Nature","Opening nature *",CustomerRules.BalanceNatures),
            CustomerEditorFields.Date("AsOfDate","Opening as-of date"),CustomerEditorFields.Text("Reference","Opening reference"),
            CustomerEditorFields.Long("Remarks","Opening draft remarks")];
        BankFields=[CustomerEditorFields.Text("BankName","Customer bank name"),CustomerEditorFields.Text("AccountHolder","Account holder"),
            CustomerEditorFields.Text("AccountNumber","Account number"),CustomerEditorFields.Text("Ifsc","IFSC"),CustomerEditorFields.Text("Branch","Branch"),
            CustomerEditorFields.Text("UpiId","UPI ID"),CustomerEditorFields.Long("ReferenceNotes","Payment reference notes")];
        NotesFields=[CustomerEditorFields.Long("Notes","Customer notes"),CustomerEditorFields.Long("InternalRemarks","Internal remarks — Super Admin only")];
        foreach(var (_,fields) in Groups) foreach(var field in fields) field.Changed+=_=>{if(!loading&&EditorOpen){Dirty=true;OnPropertyChanged(nameof(CreditWarning));}};
        RefreshCommand=new AsyncRelayCommand(()=>RefreshAsync(true),()=>CanBrowse);
        AddCommand=new RelayCommand(()=>{if(CanBrowse&&company is not null && Can("Create")) Open(null,false);},()=>CanBrowse&&company is not null&&Can("Create"));
        EditCommand=new AsyncRelayCommand(()=>OpenSelected(false),()=>CanBrowse&&Selected is not null&&Can("Edit"));
        ViewCommand=new AsyncRelayCommand(()=>OpenSelected(true),()=>CanBrowse&&Selected is not null&&Can("View"));
        EditViewedCommand=new AsyncRelayCommand(()=>OpenById(editing?.Id,false),
            ()=>EditorOpen&&ViewOnly&&editing is not null&&!Busy&&!lifetime.IsStopping&&!disposed&&Can("Edit"));
        StatusCommand=new AsyncRelayCommand<string>(ChangeStatus, _=>CanBrowse&&Selected is not null&&Can("StatusChange"));
        SaveCommand=new AsyncRelayCommand(Save,()=>CanEdit);
        CopyBillingCommand=new RelayCommand(CopyBilling,()=>CanEdit && GraftexLayout);
        foreach(var (prefix,fields) in Groups)
            foreach(var field in fields) field.SupplementKey=prefix+field.Name;
        System.Windows.WeakEventManager<SettingsDisplay,EventArgs>.AddHandler(SettingsDisplay.Instance,
            nameof(SettingsDisplay.Changed),(_,_)=>NotifyProfile());
        PreviousPageCommand=new AsyncRelayCommand(()=>MovePage(-1),()=>CanBrowse&&page>1);
        NextPageCommand=new AsyncRelayCommand(()=>MovePage(1),()=>CanBrowse&&page*SettingsDisplay.Current.Get(SettingsRegistry.PageSize)<total);
        AddBillingCommand=new RelayCommand(()=>AddAddress("Billing"),()=>CanEdit);
        AddShippingCommand=new RelayCommand(()=>AddAddress("Shipping"),()=>CanEdit);
        AddContactCommand=new RelayCommand(AddContact,()=>CanEdit);
        var ui=SynchronizationContext.Current;
        lifetime.ShutdownStarted+=()=>{if(ui is null||SynchronizationContext.Current==ui) Clear();else ui.Post(_=>Clear(),null);};
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanEditCredit));OnPropertyChanged(nameof(CanEditBank));OnPropertyChanged(nameof(CanSeeBank));
        OnPropertyChanged(nameof(ShowBank));CopyBillingCommand?.NotifyCanExecuteChanged();
        GeneralFields.Single(x=>x.Name=="Status").ReadOnly=!Can("StatusChange");
        NotesFields.Single(x=>x.Name=="InternalRemarks").ReadOnly=!access.IsSuperAdmin;
        RefreshCommand?.NotifyCanExecuteChanged();AddCommand?.NotifyCanExecuteChanged();EditCommand?.NotifyCanExecuteChanged();
        ViewCommand?.NotifyCanExecuteChanged();EditViewedCommand?.NotifyCanExecuteChanged();StatusCommand?.NotifyCanExecuteChanged();SaveCommand?.NotifyCanExecuteChanged();
        PreviousPageCommand?.NotifyCanExecuteChanged();NextPageCommand?.NotifyCanExecuteChanged();AddBillingCommand?.NotifyCanExecuteChanged();
        AddShippingCommand?.NotifyCanExecuteChanged();AddContactCommand?.NotifyCanExecuteChanged();
    }
    public void Clear()
    {
        if(disposed) return;
        var previous=viewCancellation;viewCancellation=new();previous.Cancel();previous.Dispose();
        CancelEditor();Customers.Clear();Selected=null;company=null;page=1;total=0;
        Message="";OnPropertyChanged(nameof(CompanyCaption));OnPropertyChanged(nameof(PageCaption));NotifyState();
    }
    public void CancelEditor()
    {
        liveSearch.Cancel();
        EditorOpen=false;editing=null;Dirty=false;Addresses.Clear();Contacts.Clear();AuditCaption="";InvoiceReadiness="";ValidationSummary="";
        loading=true;foreach(var (_,fields) in Groups) foreach(var field in fields){field.Text="";field.Error="";}loading=false;
        NotifyAddresses();OnPropertyChanged(nameof(CreditWarning));
    }
    private void QueueSearch(bool debounce)
    {
        _ = liveSearch.ScheduleAsync(debounce, () => Busy, ct =>
            CanBrowse && company is not null ? Run(async token => {page=1;await Reload(token);},ct) : Task.CompletedTask);
    }

    private async Task Run(Func<CancellationToken,Task> operation, CancellationToken caller=default)
    {
        if(Busy||lifetime.IsStopping||disposed) return;
        Busy=true;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(viewCancellation.Token,caller);
        try {await lifetime.RunAsync(operation,linked.Token);}
        catch(OperationCanceledException) when(caller.IsCancellationRequested) { }
        catch(OperationCanceledException) {Message="Operation cancelled. Refresh before assuming a save completed.";}
        catch {Message="Customer Master is unavailable. No sensitive database details are displayed.";}
        finally {Busy=false;}
    }
    private async Task<CustomerResult> Send(CustomerRequest request,CancellationToken ct)
    {
        var result=await Task.Run(()=>service.ExecuteAsync(request,ct),ct);
        ct.ThrowIfCancellationRequested();return result;
    }
    private bool Accept(CustomerResult result)
    {
        if(result.Outcome==CustomerOutcome.SessionRequired){Clear();SessionExpired?.Invoke();}
        if(result.Outcome is CustomerOutcome.NoCompany or CustomerOutcome.CompanyChanged)
        {
            CancelEditor();Customers.Clear();Selected=null;company=null;OnPropertyChanged(nameof(CompanyCaption));
        }
        Message=result.Outcome switch {
            CustomerOutcome.Success=>"Ready.",CustomerOutcome.InvalidInput=>"Correct the highlighted fields.",
            CustomerOutcome.DuplicateWarning=>"Similar customers exist. Review the warning before saving a separate record.",
            CustomerOutcome.SessionRequired=>"Session expired. Sign in again.",CustomerOutcome.Forbidden=>"Customer access is denied for this operation or company.",
            CustomerOutcome.NoCompany=>"Select an active company in Company Master.",CustomerOutcome.CompanyChanged=>"Company context changed. Customer selection was cleared; refresh.",
            CustomerOutcome.Conflict=>"The customer changed or a unique value conflicts. Cancel, refresh and review the latest data.",
            CustomerOutcome.NotFound=>"Customer not found in the current company.",CustomerOutcome.Ineligible=>"Customer is not eligible for new invoice selection.",
            _=>"Customer Master is unavailable. Verify the approved schema and storage."};
        ApplyErrors(result.Errors);
        return result.Outcome==CustomerOutcome.Success;
    }
    private void ApplyErrors(IReadOnlyDictionary<string,string>? errors)
    {
        foreach(var (prefix,fields) in Groups) foreach(var field in fields) field.Error=errors?.GetValueOrDefault(prefix+field.Name) ?? "";
        for(var i=0;i<Addresses.Count;i++) foreach(var field in Addresses[i].Fields) field.Error=errors?.GetValueOrDefault($"Addresses[{i}]."+field.Name) ?? "";
        for(var i=0;i<Contacts.Count;i++) foreach(var field in Contacts[i].Fields) field.Error=errors?.GetValueOrDefault($"Contacts[{i}]."+field.Name) ?? "";
        ValidationSummary=errors is null?"":string.Join(Environment.NewLine,errors.Select(x=>x.Key+": "+x.Value));
    }
    public Task RefreshAsync(bool resetPage=false)=>Run(async ct=>
    {
        var context=await Send(new(CustomerOperation.Context),ct);
        if(!Accept(context)) return;
        if(context.Context?.CompanyId!=company?.CompanyId){CancelEditor();Customers.Clear();Selected=null;page=1;}
        company=context.Context;OnPropertyChanged(nameof(CompanyCaption));
        if(resetPage) page=1;
        await Reload(ct);
    });
    private async Task Reload(CancellationToken ct)
    {
        if(company is null) return;
        var selectedId=Selected?.Id;
        var filter=new CustomerFilter(MasterSearch.Query(Search),StatusFilter,RegistrationFilter,page,SettingsDisplay.Current.Get(SettingsRegistry.PageSize));
        var result=await Send(new(CustomerOperation.List,company.CompanyId,Filter:filter),ct);
        if (result.Outcome==CustomerOutcome.Success &&
            (filter.Search!=MasterSearch.Query(Search) || filter.Status!=StatusFilter || filter.RegistrationType!=RegistrationFilter)) return;
        if(!Accept(result)) return;
        Customers.Clear();foreach(var item in result.Page!.Rows) Customers.Add(item);
        Selected=Customers.FirstOrDefault(x=>x.Id==selectedId);total=result.Page.Total;
        OnPropertyChanged(nameof(PageCaption));NotifyState();
    }
    private Task MovePage(int change)=>Run(async ct=>{page=Math.Max(1,page+change);await Reload(ct);});
    private Task OpenSelected(bool readOnly)=>OpenById(Selected?.Id,readOnly);
    private Task OpenById(Guid? id,bool readOnly)=>Run(async ct=>
    {
        if(company is null||id is null) return;
        var result=await Send(new(CustomerOperation.Get,company.CompanyId,id),ct);
        if(Accept(result)) Open(result.Detail!,readOnly);
    });
    private void Open(CustomerDetail? detail,bool readOnly)
    {
        loading=true;editing=detail;ViewOnly=readOnly;
        var defaults=SettingsDisplay.Current;
        var draft=detail?.Draft ?? (Can("CreditEdit")?new CustomerDraft {Credit=new CustomerCreditDraft
        {
            PaymentTerms=defaults.Get(SettingsRegistry.PaymentTerms),CreditDays=defaults.Get(SettingsRegistry.CreditDays),
            PurchaseOrderRequired=defaults.Get(SettingsRegistry.RequirePo)
        }}:new CustomerDraft());
        CustomerEditorFields.Load(GeneralFields,draft);CustomerEditorFields.Load(TaxFields,draft);CustomerEditorFields.Load(NotesFields,draft);
        if(detail is null) GeneralFields.Single(x=>x.Name=="Code").Text="Assigned on save";
        CustomerEditorFields.Load(CreditFields,draft.Credit);CustomerEditorFields.Load(OpeningFields,draft.Opening);
        CustomerEditorFields.Load(BankFields,draft.Bank ?? new CustomerBankDraft());
        Addresses.Clear();Contacts.Clear();
        foreach(var item in draft.Addresses) Attach(new CustomerEntryEditor(item,RetireEntry));
        foreach(var item in draft.Contacts) Attach(new CustomerEntryEditor(item,RetireEntry));
        AuditCaption=detail is null?"New customer — attribution is assigned by the authenticated service.":
            $"Created {detail.CreatedUtc:u} by {detail.CreatedBy}. Updated {detail.UpdatedUtc:u} by {detail.UpdatedBy}. Version {detail.Version}. Opening draft {detail.OpeningDraftId} / {detail.OpeningDraftVersion}.";
        InvoiceReadiness=detail is null?"Invoice eligibility is resolved from persisted master data.":detail.Eligibility.CanSelectForNewInvoice?
            "Master data ready for future invoice selection; live receivables checks remain unavailable.":string.Join(" ",detail.Eligibility.MissingOrBlockingDetails);
        ValidationSummary="";Dirty=false;EditorOpen=true;loading=false;NotifyAddresses();OnPropertyChanged(nameof(CreditWarning));
    }
    private void Attach(CustomerEntryEditor entry)
    {
        (entry.IsAddress?Addresses:Contacts).Add(entry);
        foreach(var field in entry.Fields) field.Changed+=changed=>EntryChanged(entry,changed);
    }
    private void EntryChanged(CustomerEntryEditor entry,CustomerField changed)
    {
        if(loading) return;
        Dirty=true;
        if(changingDefaults) return;
        changingDefaults=true;
        try
        {
            if(changed.Name=="IsActive"&&!changed.Checked)
            {
                if(entry.IsAddress){entry.Field("IsDefaultBilling").Checked=false;entry.Field("IsDefaultShipping").Checked=false;}
                else entry.Field("IsPrimary").Checked=false;
            }
            if(changed.Checked&&changed.Name is "IsDefaultBilling" or "IsDefaultShipping" or "IsPrimary")
                foreach(var other in (entry.IsAddress?Addresses:Contacts).Where(x=>x!=entry)) other.Field(changed.Name).Checked=false;
        }
        finally {changingDefaults=false;}
        if(changed.Name=="AddressType") NotifyAddresses();
    }
    private void NotifyProfile()
    {
        foreach(var property in new[]{nameof(GraftexLayout),nameof(StandardBilling),nameof(CombinedAddresses),nameof(ShowTax),
            nameof(ShowBank),nameof(ShowContacts),nameof(ShowCredit),nameof(ShowOpening),nameof(ShowNotes)})
            OnPropertyChanged(property);
        CopyBillingCommand.NotifyCanExecuteChanged();
    }
    private void NotifyAddresses(){OnPropertyChanged(nameof(BillingAddresses));OnPropertyChanged(nameof(ShippingAddresses));}
    private void CopyBilling()
    {
        if(!CanEdit || !GraftexLayout) return;
        var shipping=ShippingAddresses.Where(entry=>entry.Field("IsActive").Checked).ToArray();
        var billing=BillingAddresses.Where(entry=>entry.Field("IsActive").Checked).ToArray();
        var source=billing.Contains(CopySource)?CopySource:billing.Length==1?billing[0]:null;
        if(source is null){Message="Select a billing address to copy.";return;}
        var destination=shipping.Contains(CopyDestination)?CopyDestination:shipping.Length==1?shipping[0]:null;
        if(destination is null && shipping.Length>1){Message="Select the shipping address to replace.";return;}
        if(destination is not null && ConfirmShippingReplacement?.Invoke()!=true) return;
        if(destination is null || destination.Field("AddressType").Text=="Both")
        {
            var original=destination;
            destination=new CustomerEntryEditor(new CustomerAddressDraft {AddressType="Shipping",Label="Shipping address"},RetireEntry);
            Attach(destination);
            if(original?.Field("IsDefaultShipping").Checked==true || shipping.Length==0)
                destination.Field("IsDefaultShipping").Checked=true;
        }
        foreach(var field in source.Fields.Where(field=>field.Name is not ("Label" or "AddressType" or "IsActive" or "IsDefaultBilling" or "IsDefaultShipping")))
            destination.Field(field.Name).Text=field.Text;
        Dirty=true;
        NotifyAddresses();
        Message="Billing address copied to the shipping draft.";
    }
    private void AddAddress(string type)
    {
        if(!CanEdit) return;
        var role=type=="Billing"?BillingAddresses:ShippingAddresses;
        var first=GraftexLayout && !role.Any(entry=>entry.Field("IsActive").Checked);
        Attach(new CustomerEntryEditor(new CustomerAddressDraft {AddressType=type,Label=type+" address",
            RecipientName=GeneralFields.Single(x=>x.Name=="LegalName").Text,
            IsDefaultBilling=first && type=="Billing",IsDefaultShipping=first && type=="Shipping"},RetireEntry));
        Dirty=true;NotifyAddresses();
    }
    private void AddContact(){if(!CanEdit)return;Attach(new CustomerEntryEditor(new CustomerContactDraft(),RetireEntry));Dirty=true;}
    private void RetireEntry(CustomerEntryEditor entry)
    {
        if(!CanEdit) return;
        if(entry.Id is null) (entry.IsAddress?Addresses:Contacts).Remove(entry);
        else entry.Field("IsActive").Checked=false;
        Dirty=true;NotifyAddresses();
    }
    private CustomerDraft BuildDraft(Dictionary<string,string> errors)
    {
        var root=CustomerEditorFields.Read<CustomerDraft>(GeneralFields.Concat(TaxFields).Concat(NotesFields),"",errors);
        return root with {
            Credit=CustomerEditorFields.Read<CustomerCreditDraft>(CreditFields,"Credit.",errors),
            Opening=CustomerEditorFields.Read<CustomerOpeningDraft>(OpeningFields,"Opening.",errors),
            Bank=CustomerEditorFields.Read<CustomerBankDraft>(BankFields,"Bank.",errors),
            Addresses=Addresses.Select((a,i)=>CustomerEditorFields.Read<CustomerAddressDraft>(a.Fields,$"Addresses[{i}].",errors) with {Id=a.Id,Version=a.Version}).ToArray(),
            Contacts=Contacts.Select((c,i)=>CustomerEditorFields.Read<CustomerContactDraft>(c.Fields,$"Contacts[{i}].",errors) with {Id=c.Id,Version=c.Version}).ToArray()};
    }
    private Task Save()=>Run(async ct=>
    {
        if(ViewOnly||company is null) return;
        var errors=new Dictionary<string,string>();var draft=BuildDraft(errors);
        if(errors.Count>0){ApplyErrors(errors);Message="Correct the numeric/date fields.";return;}
        var request=new CustomerRequest(editing is null?CustomerOperation.Create:CustomerOperation.Update,company.CompanyId,editing?.Id,editing?.Version,draft);
        var result=await Send(request,ct);
        if(result.Outcome==CustomerOutcome.DuplicateWarning && ConfirmDuplicates?.Invoke(result.Warnings!)==true)
            result=await Send(request with {AcknowledgeDuplicates=true},ct);
        if(!Accept(result)) return;
        CancelEditor();await Reload(ct);Selected=Customers.FirstOrDefault(x=>x.Id==result.Detail!.Id);
        Message=$"Customer saved as {result.Detail!.Draft.Code}.";
    });
    private Task ChangeStatus(string? status)=>Run(async ct=>
    {
        if(company is null||Selected is null||status is null) return;
        var result=await Send(new(CustomerOperation.Status,company.CompanyId,Selected.Id,Selected.Version,Status:status),ct);
        if(Accept(result)) await Reload(ct);
    });
    public void Dispose(){if(disposed)return;Clear();disposed=true;viewCancellation.Cancel();viewCancellation.Dispose();}
}

