using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public sealed partial class NumberingViewModel(NumberingService service):ObservableObject
{
    private readonly MasterSearch search=new();
    private AccessSnapshot access=AccessSnapshot.Denied;
    private Guid? company;
    private int generation;
    private bool loaded;
    private NumberingMaster master;
    public Action? SessionExpired {get;set;}
    public bool ShowSeries=>master==NumberingMaster.Series;
    public string Title=>ShowSeries?Label("SeriesTitle","Document Series Master"):Label("YearTitle","Financial Year Master");
    public string PrimaryAction=>ShowSeries?"Set default":"Set current";
    public string StatusAction=>ShowSeries?(Selected?.Entry.Active==false?"Activate":"Deactivate"):
        Selected?.Entry.Open==false?"Reopen year":"Close year";
    public IReadOnlyList<CatalogFilter> Statuses=>ShowSeries?[new("All statuses",null),new("Active",true),new("Inactive",false)]:
        [new("All statuses",null),new("Open",true),new("Closed",false)];

    [ObservableProperty] private IReadOnlyList<NumberingRow> _rows=[];
    [ObservableProperty] private NumberingRow? _selected;
    [ObservableProperty] private string _searchText="";
    [ObservableProperty] private string _message="";
    [ObservableProperty] private string _currentYear="";
    [ObservableProperty] private CatalogFilter? _filter;
    [ObservableProperty] private bool _busy;

    private static string Label(string key,string fallback)=>SettingsDisplay.Text("Numbering."+key,fallback);
    private bool Has(string action)=>access.Allows((ShowSeries?"DocumentSeries.":"FinancialYear.")+action);
    private bool CanBrowse()=>loaded && !Busy && Has("View");
    private bool CanAdd()=>CanBrowse() && company.HasValue && Has("Create");
    private bool CanView()=>CanBrowse() && Selected is not null;
    private bool CanEdit()=>CanView() && Has("Edit");
    private bool CanPreview()=>ShowSeries && CanView();
    private bool CanPrimary()=>CanView() && (ShowSeries?Selected!.Entry.Active && Has("SetDefault"):
        Selected!.Entry.Open && Has("SetCurrent"));
    private bool CanStatus()=>CanView() && Has(ShowSeries?"StatusChange":Selected!.Entry.Open?"Close":"Reopen");

    public void SetAccess(AccessSnapshot value)
    {
        if(access.EquivalentTo(value)) return;
        var changed=access.CurrentCompanyId!=value.CurrentCompanyId;
        access=value;
        if(changed || !Has("View")) Clear();
        Notify();
    }
    public void RefreshDisplay()=>OnPropertyChanged(nameof(Title));
    public void Clear()
    {
        generation++;loaded=false;search.Cancel();company=null;
        Rows=[];Selected=null;CurrentYear="";Message="";Notify();
    }
    public async Task OpenAsync(NumberingMaster value)
    {
        Clear();
        master=value; SearchText="";Filter=Statuses[0];loaded=true;
        OnPropertyChanged(nameof(ShowSeries));OnPropertyChanged(nameof(PrimaryAction));
        OnPropertyChanged(nameof(Statuses));RefreshDisplay();Notify();
        var version=generation;
        while(Busy && version==generation) await Task.Delay(30);
        if(version==generation) await RefreshAsync();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(StatusAction));
        RefreshCommand.NotifyCanExecuteChanged(); AddCommand.NotifyCanExecuteChanged(); ViewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged(); PrimaryCommand.NotifyCanExecuteChanged(); StatusCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
    }
    partial void OnBusyChanged(bool value)=>Notify();
    partial void OnSelectedChanged(NumberingRow? value)=>Notify();
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
            var response=await SendAsync(new(master,NumberingAction.List,Search:MasterSearch.Query(SearchText),Active:Filter?.Value),token);
            if(version!=generation || token.IsCancellationRequested || !Accept(response)) return;
            company=response.Company;
            CurrentYear=response.CurrentYear is { } current?Label("Current","Current financial year")+" — "+current:
                "No current financial year. Select an open year and choose Set current.";
            var selection=Selected?.Entry.Id;
            Rows=response.Rows!.Select(x=>new NumberingRow(x,master)).ToArray();
            Selected=Rows.FirstOrDefault(x=>x.Entry.Id==selection);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        finally {Busy=false;}
    }

    [RelayCommand(CanExecute=nameof(CanAdd))] private Task AddAsync()=>OpenAsync(null,false);
    [RelayCommand(CanExecute=nameof(CanView))] private Task ViewAsync()=>OpenAsync(Selected?.Entry,true);
    [RelayCommand(CanExecute=nameof(CanEdit))] private Task EditAsync()=>OpenAsync(Selected?.Entry,false);
    [RelayCommand(CanExecute=nameof(CanPreview))] private Task PreviewAsync()=>OpenAsync(Selected?.Entry,true);

    private async Task OpenAsync(NumberingEntry? row,bool readOnly)
    {
        if(company is not Guid current) return;
        var version=generation;
        Busy=true;
        try
        {
            if(row is not null)
            {
                var detail=await SendAsync(new(master,NumberingAction.View,current,row.Id));
                if(version!=generation || !Accept(detail)) return;
                row=detail.Detail;
            }
            NumberingResult? options=null;
            if(ShowSeries)
            {
                options=await SendAsync(new(master,NumberingAction.Options,current,row?.Id));
                if(version!=generation || !Accept(options)) return;
            }
            var dialog=new MeasureEditorDialog(Title,readOnly);
            if(row is null) dialog.Title=Title+" — Add";
            async Task<NumberingResult> Submit(NumberingAction action,NumberingEntry draft)
            {
                if(version!=generation) return new(CompanyOutcome.Conflict,Message:"The company or session changed. Close the editor.");
                var result=await SendAsync(new(master,action,current,row?.Id,Entry:draft));
                if(result.Outcome==CompanyOutcome.SessionRequired) Accept(result);
                return result;
            }
            if(ShowSeries) ConfigureSeries(dialog,row,options!,Submit);
            else ConfigureYear(dialog,row,Submit);
            dialog.ShowDialog();
        }
        finally {Busy=false;}
        if(!readOnly && version==generation) await RefreshAsync();
    }

    [RelayCommand(CanExecute=nameof(CanPrimary))]
    private Task PrimaryAsync()=>ChangeAsync(ShowSeries?NumberingAction.SetDefault:NumberingAction.SetCurrent);

    [RelayCommand(CanExecute=nameof(CanStatus))]
    private async Task StatusAsync()
    {
        if(Selected is not {Entry:var row}) return;
        if(!ShowSeries && !row.Open)
        {
            if(company is not Guid current) return;
            var version=generation;
            var dialog=new MeasureEditorDialog("Reopen "+row.Code,false);
            var reason=dialog.Text("Reason *","",500);
            var reopened=false;
            dialog.Save(async ()=>
            {
                if(version!=generation) return "The company or session changed.";
                var result=await SendAsync(new(master,NumberingAction.Reopen,current,row.Id,Entry:row with{Reason=reason.Text}));
                reopened=Accept(result);
                return reopened?null:Message;
            });
            Busy=true;
            try {dialog.ShowDialog();}
            finally {Busy=false;}
            if(reopened) await RefreshAsync();
            return;
        }
        var prompt=ShowSeries?(row.Active?"Deactivate the series? Its ledger and counters will remain.":"Activate this series?"):
            Label("ClosedWarning","Close this financial year administratively? New document numbers will be blocked.");
        if(MessageBox.Show(prompt,Title,MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
        await ChangeAsync(ShowSeries?NumberingAction.Status:NumberingAction.Close,flip:ShowSeries);
    }

    private async Task ChangeAsync(NumberingAction action,bool flip=false)
    {
        if(company is not Guid current || Selected is not {Entry:var row}) return;
        Busy=true;
        bool success;
        try {success=Accept(await SendAsync(new(master,action,current,row.Id,Entry:flip?row with{Active=!row.Active}:row)));}
        finally {Busy=false;}
        if(success) await RefreshAsync();
    }
    private Task<NumberingResult> SendAsync(NumberingRequest request,CancellationToken ct=default)=>Task.Run(()=>service.ExecuteAsync(request,ct),ct);
    private bool Accept(NumberingResult result)
    {
        if(result.Outcome==CompanyOutcome.SessionRequired) {Clear();SessionExpired?.Invoke();}
        Message=Error(result);
        return result.Outcome==CompanyOutcome.Success;
    }

    private static string Error(NumberingResult result)=>result.Message??result.Outcome switch
    {
        CompanyOutcome.Success=>"",CompanyOutcome.Forbidden=>"Your permissions do not allow this operation.",
        CompanyOutcome.SessionRequired=>"Your session expired. Sign in again.",
        CompanyOutcome.Conflict=>"The record changed. Refresh and try again.",
        CompanyOutcome.NotFound=>"The record is not available in this company.",
        CompanyOutcome.InvalidInput=>"Review the values and try again.",
        _=>"Financial year or series configuration is unavailable. Check database status."
    };
    private static DateOnly? Date(System.Windows.Controls.DatePicker p)=>p.SelectedDate is DateTime date?DateOnly.FromDateTime(date):null;
    private static void ConfigureYear(MeasureEditorDialog dialog,NumberingEntry? original,
        Func<NumberingAction,NumberingEntry,Task<NumberingResult>> submit)
    {
        var today=DateTime.Today;
        var april=new DateOnly(today.Month>=4?today.Year:today.Year-1,4,1);
        var row=original??new(Start:april,End:april.AddYears(1).AddDays(-1));
        var code=dialog.Text(Label("YearCode","Financial year code")+" *",row.Code,30,original is not null);
        var name=dialog.Text(Label("YearName","Financial year name")+" *",row.Name,150);
        var start=dialog.Date("Start date *",row.Start,!row.Open);
        var end=dialog.Date("End date (inclusive) *",row.End,!row.Open);
        if(original is not null)
        {
            dialog.Text("Status",row.Open?"Open":"Closed",20,true);
            dialog.Text(Label("Current","Current financial year"),row.IsCurrent?"Yes":"No",20,true);
            dialog.Text("Document series",row.Details,4400,true);
            ShowAudit(dialog,row);
        }
        dialog.Save(async ()=>ErrorOrNull(await submit(NumberingAction.Save,row with{Code=code.Text,Name=name.Text,Start=Date(start),End=Date(end)})));
    }

    private static void ConfigureSeries(MeasureEditorDialog dialog,NumberingEntry? original,NumberingResult choices,
        Func<NumberingAction,NumberingEntry,Task<NumberingResult>> submit)
    {
        var row=original??new();
        var code=dialog.Text(Label("SeriesCode","Series code")+" *",row.Code,34,original is not null);
        var name=dialog.Text(Label("SeriesName","Series name")+" *",row.Name,150);
        var years=choices.Years??[];
        var registrations=choices.Registrations??[];
        var year=dialog.Choice("Financial year *",years,years.FirstOrDefault(x=>x.Id==row.YearId),nameof(NumberingChoice.Label),locked:original is not null,searchable:true);
        var gst=dialog.Choice("GST registration *",registrations,registrations.FirstOrDefault(x=>x.Id==row.RegistrationId),nameof(NumberingChoice.Label),locked:original is not null,searchable:true);
        var types=DocumentTypes.All;
        if(original is not null && types.All(x=>x.Code!=row.DocumentType)) types=[..types,new(row.DocumentType,row.DocumentType)];
        var type=dialog.Choice(Label("DocumentType","Document type")+" *",types,types.FirstOrDefault(x=>x.Code==row.DocumentType),
            nameof(DocumentTypeChoice.Label),locked:original is not null,searchable:true);
        var prefix=dialog.Text(Label("Prefix","Prefix template"),row.Prefix,80,row.Used);
        var separator=dialog.Choice("Separator",new[]{"","/","-"},row.Separator,locked:row.Used);
        var suffix=dialog.Text("Suffix template",row.Suffix,80,row.Used);
        var padding=dialog.Text("Number padding (1–9)",row.Padding.ToString(CultureInfo.InvariantCulture),2,row.Used);
        var starting=dialog.Text(Label("Starting","Starting number"),row.Starting.ToString(CultureInfo.InvariantCulture),19,row.Used);
        if(original is not null)
        {
            dialog.Text(Label("Next","Next number"),row.Next.ToString(CultureInfo.InvariantCulture),24,true);
            dialog.Text(Label("Default","Default series"),row.IsDefault?"Yes":"No",20,true);
            dialog.Text("Status",row.Active?"Active":"Inactive",20,true);
            ShowAudit(dialog,row);
        }
        dialog.Text("Format rules","Use {Company}, {Year} or {Series} in the prefix or suffix. The sequence is inserted between them. Maximum length is 16 characters.",500,true);
        var preview=dialog.Text(Label("Preview","Preview number"),"Preview only. No number is issued.",500,true);
        NumberingEntry? Input()
        {
            if(!int.TryParse(padding.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var pad) ||
                !long.TryParse(starting.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var start)) return null;
            return row with {Code=code.Text,Name=name.Text,YearId=(year.SelectedItem as NumberingChoice)?.Id,
                RegistrationId=(gst.SelectedItem as NumberingChoice)?.Id,DocumentType=(type.SelectedItem as DocumentTypeChoice)?.Code??"",
                Prefix=prefix.Text, Separator=separator.SelectedItem as string??"",Suffix=suffix.Text,Padding=pad,Starting=start};
        }
        var previewButton=dialog.ActionButton(Label("Preview","Preview number"));
        previewButton.Click+=async (_,_) =>
        {
            if(Input() is not {} input) {preview.Text="Enter a positive whole starting number and padding.";return;}
            previewButton.IsEnabled=false;
            try
            {
                var result=await submit(NumberingAction.Preview,input);
                if(Input()==input) preview.Text=result.Preview is {} number ? number+" — preview only" : Error(result);
            }
            finally {previewButton.IsEnabled=true;}
        };
        dialog.Save(async ()=>Input() is {} input ? ErrorOrNull(await submit(NumberingAction.Save,input)) :
            "Enter a positive whole starting number and padding.");
    }
    private static void ShowAudit(MeasureEditorDialog dialog,NumberingEntry row)
    {
        dialog.Text("Created (UTC)",row.CreatedUtc?.ToString("u",CultureInfo.InvariantCulture)??"Not recorded",32,true);
        dialog.Text("Updated (UTC)",row.UpdatedUtc?.ToString("u",CultureInfo.InvariantCulture)??"Not recorded",32,true);
        if(row.ClosedUtc is DateTime closed) dialog.Text("Last closed (UTC)",closed.ToString("u",CultureInfo.InvariantCulture),32,true);
        if(row.ReopenedUtc is DateTime reopened) dialog.Text("Last reopened (UTC)",reopened.ToString("u",CultureInfo.InvariantCulture),32,true);
        if(row.Reason is not null) dialog.Text("Last reopen reason",row.Reason,500,true);
    }
    private static string? ErrorOrNull(NumberingResult result)=>result.Outcome==CompanyOutcome.Success?null:Error(result);
}
