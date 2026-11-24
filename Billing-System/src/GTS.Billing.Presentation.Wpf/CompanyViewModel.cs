using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;
using GTS.Billing.Domain;

namespace GTS.Billing.Presentation.Wpf;

public sealed record CompanyChoice(string Key,string Label);
public sealed class CompanyField : ObservableObject
{
    private readonly string defaultLabel;
    public CompanyField(string name,string label,IReadOnlyList<CompanyChoice>? choices=null)
    {
        Name=name;defaultLabel=label;Choices=choices ?? [];FilteredChoices=new(Choices);
        System.Windows.WeakEventManager<SettingsDisplay,EventArgs>.AddHandler(SettingsDisplay.Instance,nameof(SettingsDisplay.Changed),RefreshCaption);
    }
    public string Name {get;}
    public string Label=>SettingsDisplay.Field(defaultLabel);
    private void RefreshCaption(object? sender,EventArgs e)=>OnPropertyChanged(nameof(Label));
    public IReadOnlyList<CompanyChoice> Choices {get;}
    public bool IsChoice => Choices.Count>0;
    public bool IsDate => Name == "EstablishedOn";
    public bool IsSearchable => Name == "StateCode";
    public ObservableCollection<CompanyChoice> FilteredChoices {get;}
    public bool IsFiltering {get;private set;}
    public void FilterChoices(string query)
    {
        var search=query.Trim();
        var matches=Choices.Where(option=>option.Label.Contains(search,StringComparison.OrdinalIgnoreCase) ||
            option.Key.Contains(search,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(FilteredChoices.SequenceEqual(matches)) return;
        IsFiltering=true;
        try {FilteredChoices.Clear();foreach(var option in matches) FilteredChoices.Add(option);}
        finally {IsFiltering=false;}
    }
    private bool readOnly;
    public bool ReadOnly {get=>readOnly;set=>SetProperty(ref readOnly,value);}
    private string value="";
    public string Value {get=>value;set {if(SetProperty(ref this.value,value)) OnPropertyChanged(nameof(DateValue));}}
    public DateTime? DateValue
    {
        get => IsDate && DateTime.TryParseExact(Value,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,out var date) ? date : null;
        set {if(IsDate) Value=value?.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture) ?? "";}
    }
    private string error="";
    public string Error {get=>error;set=>SetProperty(ref error,value);}
}
public sealed record CompanySection(string Title,IReadOnlyList<CompanyField> Fields,int PresentationPage=0)
{
    public bool IsPresentation => PresentationPage>0;
    public string Caption=>SettingsDisplay.Section(Title);
}
public sealed record CompanyRow(CompanyDetails Details)
{
    public string Code=>Details.Draft.Code;
    public string LegalName=>Details.Draft.LegalName;
    public string? TradeName=>Details.Draft.TradeName;
    public string City=>Details.Draft.City;
    public string State=>CompanyRules.States.GetValueOrDefault(Details.Draft.StateCode,Details.Draft.StateCode);
    public string? Gstin=>Details.Draft.Gstin;
    public string Status=>Details.Active?"Active":"Inactive";
    public string Current=>Details.IsCurrent?"Current":"";
}

public sealed class CompanyViewModel : ObservableObject
{
    private readonly CompanyService service;
    private readonly ApplicationLifetime lifetime;
    private CompanyDetails? editing;
    private CompanyDraft? original;
    private readonly CompanyPresentationService? presentation;
    private readonly MasterSearch liveSearch = new();
    private bool loading;
    private int page;
    private bool more;
    public string PageCaption=>$"Page {page+1}";
    private CancellationTokenSource viewCancellation = new();
    public ObservableCollection<CompanyRow> Companies {get;}=[];
    public IReadOnlyList<CompanySection> Sections {get;}
    public IEnumerable<CompanyField> Fields=>Sections.SelectMany(x=>x.Fields);
    private CompanyRow? selected;
    public CompanyRow? Selected {get=>selected;set {SetProperty(ref selected,value);NotifyCommands();}}
    private string search="";
    public string Search
    {
        get => search;
        set
        {
            var previous = MasterSearch.Query(search);
            if (!SetProperty(ref search, value)) return;
            var query = MasterSearch.Query(value);
            if (previous != query)
                _ = liveSearch.ScheduleAsync(query.Length > 0, () => Busy, ct =>
                    !EditorVisible && !lifetime.IsStopping ? Run(async token=>{page=0;await Reload(token);},ct) : Task.CompletedTask);
        }
    }
    private string message="";
    public string Message {get=>message;private set=>SetProperty(ref message,value);}
    private string currentCompany="No company selected";
    private CompanyDetails? currentDetails;
    public string CurrentCompany {get=>currentCompany;private set=>SetProperty(ref currentCompany,value);}
    private Guid? currentCompanyId;
    public Guid? CurrentCompanyId {get=>currentCompanyId;private set=>SetProperty(ref currentCompanyId,value);}
    public void RefreshDisplay()
    {
        var company=currentDetails?.Draft;
        CurrentCompany=company is null?"No company selected":"Current Company: "+(SettingsDisplay.Current.Get(SettingsRegistry.CompanyDisplay) switch
        {
            "Code and name"=>company.Code+" — "+company.LegalName,
            "Name and code"=>company.LegalName+" ("+company.Code+")",
            _=>company.LegalName
        });
    }
    private bool busy;
    public bool Busy {get=>busy;private set {SetProperty(ref busy,value);OnPropertyChanged(nameof(CanEdit));NotifyCommands();}}
    private bool editorVisible;
    public bool EditorVisible {get=>editorVisible;private set=>SetProperty(ref editorVisible,value);}
    private bool viewOnly;
    public bool ViewOnly {get=>viewOnly;private set {SetProperty(ref viewOnly,value);OnPropertyChanged(nameof(CanEdit));}}
    private AccessSnapshot access=AccessSnapshot.Denied;
    private bool CanViewCompany(CompanyDetails? details)=>details is not null &&
        (access.IsSuperAdmin || details.IsCurrent && access.Allows("Company.View",details.Id));
    private bool CanEditCompany(CompanyDetails? details)=>CanViewCompany(details) && (access.IsSuperAdmin ||
        new[]{"Edit","BankManage","BrandingManage","InvoiceSettingsManage"}.Any(x=>access.Allows("Company."+x,details!.Id)));
    public bool CanEdit=>!Busy&&!ViewOnly&&!lifetime.IsStopping &&
        (editing is null?access.IsSuperAdmin:access.Allows("Company.Edit",editing.Id));
    public void SetAccess(AccessSnapshot value)
    {
        if(access.EquivalentTo(value)) return;
        if(EditorVisible && !(access.IsSuperAdmin && value.IsSuperAdmin)) CancelEditor();
        access=value;
        OnPropertyChanged(nameof(CanEdit));
        NotifyCommands();
    }
    private bool dirty;
    public bool Dirty {get=>dirty;private set=>SetProperty(ref dirty,value);}
    public event Action? SessionExpired;
    private CompanyPresentationViewModel? presentationEditor;
    public CompanyPresentationViewModel? PresentationEditor
    {
        get => presentationEditor;
        private set => SetProperty(ref presentationEditor,value);
    }
    public bool HasUnsavedChanges => Dirty || PresentationEditor?.HasPendingChanges==true;
    public string PresentationUnavailable => editing is null
        ? "Save the company first, then select it as Current to manage this tab."
        : "Select this active company as Current to access this tab.";
    public bool CanEditViewed => EditorVisible && ViewOnly && !Busy && !lifetime.IsStopping && CanEditCompany(editing);
    private void SetPresentationEditor(CompanyDetails? details,bool readOnly)
    {
        PresentationEditor?.Dispose();
        if (presentation is null || details is not { Active:true, IsCurrent:true } || !CanViewCompany(details))
        {
            PresentationEditor=null;
        }
        else
        {
            PresentationEditor=new(presentation)
            {
                CompanyId=details.Id,
                CompanyName=details.Draft.LegalName,
                ReadOnly=readOnly,
                Access=access,
                SessionExpired=() => { Clear(); SessionExpired?.Invoke(); },
                CompanyVersionChanged=version =>
                {
                    if (editing?.Id==details.Id && editing.Version==version-1) editing=editing with { Version=version };
                }
            };
        }
        OnPropertyChanged(nameof(PresentationUnavailable));
    }
    public IAsyncRelayCommand RefreshCommand {get;}
    public IRelayCommand AddCommand {get;}
    public IAsyncRelayCommand EditCommand {get;}
    public IAsyncRelayCommand ViewCommand {get;}
    public IAsyncRelayCommand ToggleActiveCommand {get;}
    public IAsyncRelayCommand SelectCommand {get;}
    public IAsyncRelayCommand SaveCommand {get;}
    public IAsyncRelayCommand EditViewedCommand {get;}
    public IAsyncRelayCommand PreviousPageCommand {get;}
    public IAsyncRelayCommand NextPageCommand {get;}

    public CompanyViewModel(CompanyService service,ApplicationLifetime lifetime,CompanyPresentationService? presentation=null)
    {
        this.service=service;this.lifetime=lifetime;this.presentation=presentation;
        CompanyField F(string name,string label)=>new(name,label,name=="StateCode"?
            CompanyRules.States.Select(x=>new CompanyChoice(x.Key,x.Key+" — "+x.Value)).ToArray():
            name=="RegistrationType"?CompanyRules.RegistrationTypes.Select(x=>new CompanyChoice(x,x)).ToArray():null);
        Sections=[
            new("General", [F("Code","Company code * (immutable after creation)"),F("LegalName","Legal company name *"),
                F("TradeName","Trade / display name"),F("Tagline","Business description / tagline"),F("BusinessType","Business type"),
                F("EstablishedOn","Established on")]),
            new("Address",[F("AddressLine1","Address line 1 *"),F("AddressLine2","Address line 2"),F("Locality","Area / locality"),
                F("City","City *"),F("District","District"),F("StateCode","State / UT *"),F("Pin","PIN *"),F("Country","Country code * (IN)")]),
            new("Contacts",[F("Mobile","Primary mobile"),F("AlternateMobile","Alternate mobile"),F("Landline","Landline"),
                F("Email","Email"),F("Website","Website (http/https)"),F("ContactPerson","Contact person")]),
            new("Tax profile",[F("RegistrationType","GST registration type *"),F("Pan","PAN"),
                F("Tan","TAN"),F("Cin","CIN / LLPIN"),F("Udyam","MSME / Udyam"),F("Iec","IEC No.")]),
            new("Logos & Branding",[],1),
            new("Bank Accounts",[],2),
            new("Invoice Defaults",[],3),
            new("GST Registrations",[],4)
        ];
        foreach(var field in Fields) field.PropertyChanged+=(_,e)=>
        {if(e.PropertyName==nameof(CompanyField.Value)&&!loading&&EditorVisible) Dirty=original is null || Draft()!=original;};
        RefreshCommand=new AsyncRelayCommand(RefreshAsync,()=>!EditorVisible&&!Busy&&!lifetime.IsStopping);
        AddCommand=new RelayCommand(()=>Open(null,false),()=>access.IsSuperAdmin&&!EditorVisible&&!Busy&&!lifetime.IsStopping);
        EditCommand=new AsyncRelayCommand(()=>OpenSelected(false),()=>!EditorVisible&&!Busy&&!lifetime.IsStopping&&CanEditCompany(Selected?.Details));
        ViewCommand=new AsyncRelayCommand(()=>OpenSelected(true),()=>!EditorVisible&&!Busy&&!lifetime.IsStopping&&CanViewCompany(Selected?.Details));
        ToggleActiveCommand=new AsyncRelayCommand(()=>Mutate(CompanyAction.SetActive),()=>access.IsSuperAdmin&&!EditorVisible&&Selected is not null&&!Busy&&!lifetime.IsStopping);
        SelectCommand=new AsyncRelayCommand(()=>Mutate(CompanyAction.Select),()=>!EditorVisible&&Selected?.Details.Active==true&&!Busy&&!lifetime.IsStopping);
        SaveCommand=new AsyncRelayCommand(SaveAsync,()=>CanEdit&&EditorVisible);
        EditViewedCommand=new AsyncRelayCommand(()=>OpenById(editing?.Id,false),()=>CanEditViewed);
        PreviousPageCommand=new AsyncRelayCommand(()=>Run(async token=>{page--;await Reload(token);}),
            ()=>!EditorVisible&&!Busy&&!lifetime.IsStopping&&page>0);
        NextPageCommand=new AsyncRelayCommand(()=>Run(async token=>{page++;await Reload(token);}),
            ()=>!EditorVisible&&!Busy&&!lifetime.IsStopping&&more);
        var ui = SynchronizationContext.Current;
        lifetime.ShutdownStarted+=()=> { if(ui is null || SynchronizationContext.Current==ui) Clear(); else ui.Post(_=>Clear(),null); };
    }
    private void NotifyCommands()
    {
        RefreshCommand?.NotifyCanExecuteChanged();AddCommand?.NotifyCanExecuteChanged();EditCommand?.NotifyCanExecuteChanged();
        ViewCommand?.NotifyCanExecuteChanged();ToggleActiveCommand?.NotifyCanExecuteChanged();SelectCommand?.NotifyCanExecuteChanged();
        SaveCommand?.NotifyCanExecuteChanged();EditViewedCommand?.NotifyCanExecuteChanged();
        PreviousPageCommand?.NotifyCanExecuteChanged();NextPageCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PageCaption));
        OnPropertyChanged(nameof(CanEditViewed));
    }
    private CompanyDraft Draft()
    {
        var result=new CompanyDraft();
        foreach(var field in Fields) typeof(CompanyDraft).GetProperty(field.Name)!.SetValue(result,
            string.IsNullOrWhiteSpace(field.Value)?null:field.Value);
        // Required string properties never cross the service boundary as null.
        return result with {Code=result.Code ?? "",LegalName=result.LegalName ?? "",AddressLine1=result.AddressLine1 ?? "",
            City=result.City ?? "",StateCode=result.StateCode ?? "",Pin=result.Pin ?? "",Country=result.Country ?? "",
            RegistrationType=result.RegistrationType ?? ""};
    }
    private void Open(CompanyDetails? details,bool readOnly)
    {
        loading=true;editing=details;original=details is null?null:details.Draft with {Gstin=null};ViewOnly=readOnly;
        var draft=original ?? new CompanyDraft();
        foreach(var field in Fields) {if(field.IsSearchable) field.FilterChoices("");field.Value=(string?)typeof(CompanyDraft).GetProperty(field.Name)!.GetValue(draft) ?? "";field.Error="";field.ReadOnly=field.Name=="Code"&&details is not null;}
        Dirty=false;EditorVisible=true;
        SetPresentationEditor(details,readOnly);
        loading=false;NotifyCommands();
    }
    public void CancelEditor()
    {
        liveSearch.Cancel();
        EditorVisible=false;editing=null;original=null;Dirty=false;
        SetPresentationEditor(null,false);
        foreach(var field in Fields) {field.Value="";field.Error="";}
        NotifyCommands();
    }
    public void Clear()
    {
        var previous = viewCancellation;
        viewCancellation = new();
        previous.Cancel();
        previous.Dispose();
        CancelEditor();Companies.Clear();Selected=null;currentDetails=null;CurrentCompanyId=null;page=0;more=false;RefreshDisplay();Message="";
    }
    private async Task Run(Func<CancellationToken,Task> operation, CancellationToken caller = default)
    {
        if(Busy||lifetime.IsStopping) return;
        Busy=true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(viewCancellation.Token, caller);
        try { await lifetime.RunAsync(operation, linked.Token); }
        catch(OperationCanceledException) when(caller.IsCancellationRequested) { }
        catch(OperationCanceledException) {Message="Operation cancelled. Refresh to check whether a save completed.";}
        catch {Message="Company Master is unavailable. No raw database details are shown.";}
        finally {Busy=false;}
    }
    private async Task<CompanyResult> Send(CompanyRequest request,CancellationToken ct)
    {
        var result = await Task.Run(()=>service.ExecuteAsync(request,ct),ct);
        ct.ThrowIfCancellationRequested();
        return result;
    }
    private bool Accept(CompanyResult result)
    {
        if(result.Outcome==CompanyOutcome.SessionRequired) {Clear();SessionExpired?.Invoke();}
        Message=result.Outcome switch {
            CompanyOutcome.Success=>"Ready.",CompanyOutcome.SessionRequired=>"Session expired. Sign in again.",
            CompanyOutcome.Forbidden=>"Company access is denied for this operation.",
            CompanyOutcome.Conflict=>"The data changed or conflicts with an existing record. Refresh before editing.",
            CompanyOutcome.InvalidInput=>"Check the highlighted fields or the company status.",
            CompanyOutcome.NotFound=>"Company not found.",_=>"Company Master is unavailable. Contact the administrator."};
        foreach(var field in Fields) field.Error=result.Errors?.GetValueOrDefault(field.Name) ?? "";
        return result.Outcome==CompanyOutcome.Success;
    }
    private async Task Reload(CancellationToken ct)
    {
        var selectedId=Selected?.Details.Id;
        var query=MasterSearch.Query(Search);
        var result=await Send(new(CompanyAction.List,Search:query,ProfileOnly:true,Page:page,
            PageSize:SettingsDisplay.Current.Get(SettingsRegistry.PageSize)),ct);
        if (query!=MasterSearch.Query(Search) && result.Outcome==CompanyOutcome.Success) return;
        if(!Accept(result)) {Companies.Clear();Selected=null;more=false;SetCurrent(null);return;}
        more=result.More;
        Companies.Clear();foreach(var row in result.Companies!) Companies.Add(new(row));
        Selected=Companies.FirstOrDefault(x=>x.Details.Id==selectedId);
        var current=await Send(new(CompanyAction.Current,ProfileOnly:true),ct);
        if(Accept(current)) SetCurrent(current.Company);
    }
    public Task RefreshAsync()=>Run(async ct=>{page=0;await Reload(ct);});
    public Task RefreshCurrentAsync()=>Run(async ct=>{
        var result=await Send(new(CompanyAction.Current,ProfileOnly:true),ct);
        if(Accept(result)) SetCurrent(result.Company);
    });
    private void SetCurrent(CompanyDetails? details) { currentDetails=details;CurrentCompanyId=details?.Id;RefreshDisplay(); }
    private Task OpenSelected(bool readOnly)=>OpenById(Selected?.Details.Id,readOnly);
    private Task OpenById(Guid? id,bool readOnly)=>Run(async ct=>{
        if(id is null) return;
        var result=await Send(new(CompanyAction.Get,id,ProfileOnly:true),ct);
        if(Accept(result)) Open(result.Company!,readOnly);
    });
    private Task SaveAsync()=>Run(async ct=>{
        if(ViewOnly) return;
        var result=await Send(new(editing is null?CompanyAction.Create:CompanyAction.Update,editing?.Id,Draft(),editing?.Version,
            ProfileOnly:true),ct);
        if(!Accept(result)) return;
        var savedId=result.Company!.Id;
        CancelEditor();await Reload(ct);Selected=Companies.FirstOrDefault(x=>x.Details.Id==savedId);Message="Company saved.";
    });
    private Task Mutate(CompanyAction action)=>Run(async ct=>{
        if(Selected is null) return;
        var row=Selected.Details;
        var result=await Send(new(action,row.Id,ExpectedVersion:row.Version,Active:!row.Active,ProfileOnly:true),ct);
        if(Accept(result)) await Reload(ct);
    });
}

