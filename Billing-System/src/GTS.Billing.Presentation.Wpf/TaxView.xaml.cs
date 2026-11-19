using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class TaxView : UserControl
{
    public TaxView() => InitializeComponent();

    internal void ConfigureColumns(TaxMaster master)
    {
        Records.Columns.Clear();
        void Text(string title,string path,int width=130,string? format=null,string? key=null)
        {
            var col=new DataGridTextColumn {Header=title,Binding=new Binding(path){StringFormat=format},
                Width=width==0?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(width),MinWidth=85};
            if(key is not null) SettingsDisplay.SetLabelKey(col,"Tax."+key);
            Records.Columns.Add(col);
        }
        if(master==TaxMaster.Classifications)
        {
            Text("Type","Type",90);
            Text("Code","Code",115);
            Text("Description","Description",0);
            Text("Effective rate summary","Summary",260);
        }
        else if(master==TaxMaster.Rates)
        {
            Text("Rate code","Code",110);
            Text("Rate name","Name",0);
            Text("Tax treatment","TreatmentLabel",145,key:"Treatment");
            Text("GST rate","Total",95,"{0:G29}%", "Rate");
        }
        else
        {
            Text("Classification","Code",145,key:"Classification");
            Text("Rate code","Name",130);
            Text("Context","Context",0);
        }
        if(master!=TaxMaster.Classifications)
        {
            Text("Effective from","From",130,"{0:d}","From");
            Text("Effective to (excluded)","To",155,"{0:d}","To");
        }
        Text("Status","Status",105);
    }
}

public sealed record TaxGridRow(TaxEntry Entry)
{
    public string Type=>Entry.Type;
    public string Code=>Entry.Code;
    public string Name=>Entry.Name;
    public string? Description=>Entry.Description;
    public string? Summary=>Entry.Summary;
    public string TreatmentLabel=>TaxEditor.Treatment(Entry.Treatment);
    public decimal Total=>Entry.Total;
    public DateOnly? From=>Entry.From;
    public DateOnly? To=>Entry.To;
    public string Context=>Entry.Context;
    public string Status=>Entry.Active?"Active":"Inactive";
}

public sealed partial class TaxViewModel(TaxCatalogService service) : ObservableObject
{
    private readonly MasterSearch search=new();
    private AccessSnapshot access=AccessSnapshot.Denied;
    private Guid? company;
    private int generation;
    private bool loaded;
    private TaxMaster master;
    public Action? SessionExpired {get;set;}
    public IReadOnlyList<CatalogFilter> Statuses {get;}=[new("All statuses",null),new("Active",true),new("Inactive",false)];
    public string Title=>master switch
    {
        TaxMaster.Classifications=>SettingsDisplay.Text("Navigation.HsnSacMaster","HSN/SAC Master"),
        TaxMaster.Rates=>SettingsDisplay.Text("Navigation.TaxMaster","GST Rate Master"),
        _=>SettingsDisplay.Text("Navigation.TaxAssignments","Classification rate assignments")
    };
    public string EditCaption=>master==TaxMaster.Assignments?"Manage effective end date":
        master==TaxMaster.Rates?"Edit metadata / end date":"Edit";

    [ObservableProperty] private IReadOnlyList<TaxGridRow> _rows=[];
    [ObservableProperty] private TaxGridRow? _selected;
    [ObservableProperty] private string _searchText="";
    [ObservableProperty] private string _message="";
    [ObservableProperty] private CatalogFilter? _filter;
    [ObservableProperty] private bool _busy;

    private bool Has(string action)=>access.Allows((master==TaxMaster.Classifications?"TaxClassification.":"GstRate.")+action);
    private bool CanBrowse()=>loaded && !Busy && Has("View");
    private bool CanAdd()=>CanBrowse() && company.HasValue && Has(master==TaxMaster.Assignments?"Assign":"Create");
    private bool CanView()=>CanBrowse() && Selected is not null;
    private bool CanEdit()=>CanView() && Has(master==TaxMaster.Assignments?"Assign":"Edit");
    private bool CanStatus()=>CanView() && master!=TaxMaster.Assignments && Has("StatusChange");
    public void SetAccess(AccessSnapshot value)
    {
        if(access.EquivalentTo(value)) return;
        var changed=access.CurrentCompanyId!=value.CurrentCompanyId;
        access=value;
        if(changed || !Has("View")) Clear();
        Notify();
    }
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Rows));
    }
    public void Clear()
    {
        generation++; loaded=false; search.Cancel(); company=null;
        Rows=[]; Selected=null; Message="";
        Notify();
    }

    public async Task OpenAsync(TaxMaster value)
    {
        Clear();
        master=value; SearchText=""; Filter=Statuses[0]; loaded=true;
        OnPropertyChanged(nameof(EditCaption));
        RefreshDisplay();
        var version=generation;
        while(Busy && version==generation) await Task.Delay(30);
        if(version==generation) await RefreshAsync();
    }

    private void Notify()
    {
        RefreshCommand.NotifyCanExecuteChanged(); AddCommand.NotifyCanExecuteChanged();
        ViewCommand.NotifyCanExecuteChanged(); EditCommand.NotifyCanExecuteChanged(); StatusCommand.NotifyCanExecuteChanged();
    }
    partial void OnBusyChanged(bool value)=>Notify();
    partial void OnSelectedChanged(TaxGridRow? value)=>Notify();
    partial void OnFilterChanged(CatalogFilter? value)=>Schedule(false);
    partial void OnSearchTextChanged(string? oldValue,string newValue)
    {
        if(MasterSearch.Query(oldValue)!=MasterSearch.Query(newValue)) Schedule(MasterSearch.Query(newValue).Length>0);
    }
    private void Schedule(bool debounce)
    {
        if(loaded) _=search.ScheduleAsync(debounce,()=>Busy,RefreshCoreAsync);
    }

    [RelayCommand(CanExecute=nameof(CanBrowse))]
    private Task RefreshAsync()=>RefreshCoreAsync(CancellationToken.None);
    private async Task RefreshCoreAsync(CancellationToken token)
    {
        if(!CanBrowse()) return;
        Busy=true;
        var version=generation;
        try
        {
            var response=await SendAsync(new(master,TaxAction.List,Search:MasterSearch.Query(SearchText),Active:Filter?.Value),token);
            if(version!=generation || token.IsCancellationRequested || !Accept(response)) return;
            company=response.Company;
            var current=Selected?.Entry.Id;
            Rows=response.Rows!.Select(x=>new TaxGridRow(x)).ToArray();
            Selected=Rows.FirstOrDefault(x=>x.Entry.Id==current);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        finally {Busy=false;}
    }

    [RelayCommand(CanExecute=nameof(CanAdd))] private Task AddAsync()=>OpenRecordAsync(null,false);
    [RelayCommand(CanExecute=nameof(CanView))] private Task ViewAsync()=>OpenRecordAsync(Selected?.Entry,true);
    [RelayCommand(CanExecute=nameof(CanEdit))] private Task EditAsync()=>OpenRecordAsync(Selected?.Entry,false);

    private async Task OpenRecordAsync(TaxEntry? row,bool readOnly)
    {
        if(company is not Guid current) return;
        var version=generation;
        Busy=true;
        try
        {
            if(row is not null)
            {
                var response=await SendAsync(new(master,TaxAction.View,current,row.Id));
                if(version!=generation || !Accept(response)) return;
                row=response.Detail;
            }
            TaxResult? options=null;
            if(master==TaxMaster.Assignments)
            {
                options=await SendAsync(new(master,TaxAction.Options,current,row?.Id));
                if(version!=generation || !Accept(options)) return;
            }
            var dialog=new MeasureEditorDialog(Title,readOnly);
            if(row is null) dialog.Title=Title+" — Add";
            TaxEditor.Configure(dialog,master,row,options,async input=>
            {
                if(version!=generation) return "Company or session changed. Close this editor.";
                var response=await SendAsync(new(master,TaxAction.Save,current,row?.Id,Entry:input));
                return Accept(response)?null:Message;
            });
            dialog.ShowDialog();
        }
        finally {Busy=false;}
        if(!readOnly && version==generation) await RefreshAsync();
    }

    [RelayCommand(CanExecute=nameof(CanStatus))]
    private async Task StatusAsync()
    {
        if(company is not Guid current || Selected is not {Entry:var row}) return;
        if(MessageBox.Show(row.Active?"Deactivate this record for new selections? Existing links remain.":"Activate this record?",
            Title,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
        Busy=true;
        bool changed;
        try {changed=Accept(await SendAsync(new(master,TaxAction.Status,current,row.Id,Entry:row with {Active=!row.Active}))); }
        finally {Busy=false;}
        if(changed) await RefreshAsync();
    }

    private Task<TaxResult> SendAsync(TaxRequest request,CancellationToken ct=default)=>Task.Run(()=>service.ExecuteAsync(request,ct),ct);
    private bool Accept(TaxResult response)
    {
        if(response.Outcome==CompanyOutcome.SessionRequired) { Clear(); SessionExpired?.Invoke(); }
        Message=Error(response);
        return response.Outcome==CompanyOutcome.Success;
    }
    internal static string Error(TaxResult response)=>response.Message??response.Outcome switch
    {
        CompanyOutcome.Success=>"",
        CompanyOutcome.Forbidden=>"Your permissions do not allow this operation.",
        CompanyOutcome.SessionRequired=>"Your session expired. Sign in again.",
        CompanyOutcome.Conflict=>"The record changed. Refresh and try again.",
        CompanyOutcome.NotFound=>"The record is unavailable in this company.",
        CompanyOutcome.InvalidInput=>"Review the values and try again.",
        _=>"The tax catalog is unavailable. Check the development database status."
    };
}
