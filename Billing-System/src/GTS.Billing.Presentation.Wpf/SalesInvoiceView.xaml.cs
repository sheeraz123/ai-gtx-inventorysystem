using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class SalesInvoiceView : UserControl
{
    public SalesInvoiceView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>{SettingsDisplay.Instance.Changed+=SettingsChanged;ApplyLayout();};
        Unloaded+=(_,_)=>SettingsDisplay.Instance.Changed-=SettingsChanged;
    }

    private void SettingsChanged(object? sender,EventArgs args)=>ApplyLayout();
    internal static SalesListConfiguration.Rule Rule(string key)
    {
        var definition=SalesListConfiguration.Items.Single(x=>x.Key=="SalesList."+key);
        return SettingsDisplay.Current.Get(definition);
    }
    internal static bool VisibleRule(string key)=>Rule(key).Visible;

    private void ApplyLayout()
    {
        var settings=SettingsDisplay.Current;
        var layout=settings.Get(SettingsRegistry.InvoiceListLayout);
        var classic=layout==SalesListConfiguration.Classic;
        var split=layout==SalesListConfiguration.Split;
        TitleBanner.Padding=new Thickness(12,classic?5:12,12,classic?5:12);
        InvoiceGrid.MinRowHeight=classic?24:32;
        InvoiceGrid.RowHeight=classic?26:34;
        DetailColumn.Width=split?new GridLength(320):new GridLength(0);
        DetailColumn.MinWidth=split?46:0;
        DetailDivider.Visibility=DetailPanel.Visibility=split?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumnSpan(InvoiceGrid,split?1:3);
        if(split) DetailPanel.IsExpanded=true;

        BuildToolbar(FilterHost,"Filters",settings.Get(SettingsRegistry.InvoiceFilterArrangement)=="Stacked",
            (id,rule)=>CreateFilter(id,rule,classic));
        BuildToolbar(ActionHost,"Actions",settings.Get(SettingsRegistry.InvoiceActionArrangement)=="Stacked",
            (id,rule)=>CreateAction(id,rule));

        InvoiceGrid.Columns.Clear();
        foreach(var (id,rule) in Rules("Columns"))
        {
            var numeric=id is "Tax" or "Taxable" or "Total";
            var column=new DataGridTextColumn {Header=rule.Label,Width=rule.Width,
                Binding=new Binding(id) {StringFormat=numeric?"N2":id=="Date"?"dd/MM/yyyy":null}};
            if(numeric)
            {
                var style=new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Right));
                style.Setters.Add(new Setter(MarginProperty,new Thickness(0,0,8,0)));
                column.ElementStyle=style;
            }
            InvoiceGrid.Columns.Add(column);
        }
    }

    private static IEnumerable<(string Id,SalesListConfiguration.Rule Rule)> Rules(string group)=>
        SalesListConfiguration.Items.Where(item=>item.Key.StartsWith("SalesList."+group+".",StringComparison.Ordinal))
            .Select(item=>(Id:item.Key.Split('.')[2],Rule:SettingsDisplay.Current.Get(item)))
            .Where(item=>item.Rule.Visible).OrderBy(item=>item.Rule.Order);

    private static void BuildToolbar(ContentControl host,string group,bool stacked,
        Func<string,SalesListConfiguration.Rule,FrameworkElement> create)
    {
        Panel panel=stacked?new StackPanel():new WrapPanel();
        foreach(var (id,rule) in Rules(group)) panel.Children.Add(create(id,rule));
        host.Content=panel;
    }

    private static FrameworkElement CreateFilter(string id,SalesListConfiguration.Rule rule,bool classic)
    {
        Control input;
        if(id is "From" or "To")
        {
            var date=new DatePicker();
            date.SetBinding(DatePicker.SelectedDateProperty,id);
            input=date;
        }
        else if(id is "Search" or "Customer" or "Number")
        {
            var text=new TextBox {MaxLength=100,Padding=new Thickness(6,4,6,4)};
            text.SetBinding(TextBox.TextProperty,new Binding(id=="Search"?"SearchText":id+"Text")
                {UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            input=text;
        }
        else if(id=="Category")
        {
            var items=new FrameworkElementFactory(typeof(StackPanel));
            items.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
            var categories=new ListBox {DisplayMemberPath="Label",ItemsPanel=new ItemsPanelTemplate(items),
                SelectionMode=SelectionMode.Single};
            categories.SetBinding(ItemsControl.ItemsSourceProperty,"Categories");
            categories.SetBinding(Selector.SelectedItemProperty,"Category");
            ScrollViewer.SetHorizontalScrollBarVisibility(categories,ScrollBarVisibility.Disabled);
            input=categories;
        }
        else
        {
            var combo=new ComboBox {DisplayMemberPath="Label"};
            combo.SetBinding(ItemsControl.ItemsSourceProperty,id switch
                {"Status"=>"Statuses","Year"=>"Years",_=>"SeriesChoices"});
            combo.SetBinding(Selector.SelectedItemProperty,id);
            input=combo;
        }
        input.MinHeight=classic?24:30;
        System.Windows.Automation.AutomationProperties.SetName(input,rule.Label);
        return new StackPanel {Width=id switch {"Category"=>double.NaN,"Customer" or "Search"=>180,_=>135},
            Margin=new Thickness(0,0,10,6),
            Children={new Label {Content=rule.Label,Target=input,Padding=new Thickness(0,0,0,3)},input}};
    }

    private FrameworkElement CreateAction(string id,SalesListConfiguration.Rule rule)
    {
        var button=new Button {Content=rule.Label,Style=(Style)FindResource("InvoiceButton")};
        BillingIcon.Set(button,rule.Label,id switch
        {
            "New"=>"\uE710","View"=>"\uE890","PrintPreview"=>"\uE749","Edit"=>"\uE70F",
            "Finalize"=>"\uE73E","DeleteDraft"=>"\uE74D","CancelInvoice"=>"\uE711",_=>"\uE72C"
        });
        button.SetBinding(Button.CommandProperty,id+"Command");
        if(id=="New" && !SystemParameters.HighContrast)
        {
            button.SetResourceReference(BackgroundProperty,"Palette.InvoiceAccent");
            button.Foreground=System.Windows.Media.Brushes.White;
        }
        return button;
    }

    private void ToggleDetails(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded || DetailColumn is null || DetailPanel.Visibility!=Visibility.Visible) return;
        DetailColumn.Width=new GridLength(DetailPanel.IsExpanded?320:46);
        DetailPanel.Header=DetailPanel.IsExpanded?"Invoice details":null;
    }
}

public sealed partial class SalesInvoiceViewModel(SalesInvoiceService service,CustomerService customers):ObservableObject
{
    private readonly MasterSearch search=new();
    private readonly MasterSearch details=new();
    private AccessSnapshot access=AccessSnapshot.Denied;
    private SalesOptions? options;
    private Guid? company;
    private int generation;
    private bool loaded;
    public Action? SessionExpired {get;set;}
    public IReadOnlyList<SalesStatusFilter> Statuses {get;}=[new("All statuses"),new("Finalized",true,false),new("Draft",false),
        new("Cancelled",true,true)];
    public IReadOnlyList<SalesCategoryFilter> Categories {get;}=[new("All Bills",null),new("Local Sale",SalesIntent.Local),
        new("Inter State Sale",SalesIntent.Interstate)];
    private static readonly SalesChoice All=new(Guid.Empty,"All");
    public IReadOnlyList<SalesChoice> Years=>[All,..options?.Years??[]];
    public IReadOnlyList<SalesChoice> Customers=>[All,..options?.Customers??[]];
    public IReadOnlyList<SalesChoice> SeriesChoices=>[All,..options?.Series??[]];
    [ObservableProperty] private IReadOnlyList<SalesRow> _rows=[];
    [ObservableProperty] private SalesRow? _selected;
    [ObservableProperty] private string _searchText="";
    [ObservableProperty] private string _numberText="";
    [ObservableProperty] private string _customerText="";
    [ObservableProperty] private SalesCategoryFilter? _category;
    [ObservableProperty] private SalesInvoiceSnapshot? _selectedDetail;
    public bool IsSplit=>SettingsDisplay.Current.Get(SettingsRegistry.InvoiceListLayout)==SalesListConfiguration.Split;
    [ObservableProperty] private string _message="";
    [ObservableProperty] private SalesStatusFilter? _status;
    [ObservableProperty] private SalesChoice? _year;
    [ObservableProperty] private SalesChoice? _customer;
    [ObservableProperty] private SalesChoice? _series;
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private NoticeKind _finalizationNoticeKind;
    public bool CanDismissFinalizationNotice=>FinalizationNoticeKind is NoticeKind.Error or NoticeKind.Success;
    partial void OnFinalizationNoticeKindChanged(NoticeKind value)=>OnPropertyChanged(nameof(CanDismissFinalizationNotice));
    [RelayCommand]
    private void DismissFinalizationNotice()
    { Message="";FinalizationNoticeKind=NoticeKind.Automatic; }

    private bool Has(string action)=>access.Allows("SalesInvoice."+action);
    private bool CanBrowse()=>loaded && !Busy && Has("View") && Has("ViewFinancials");
    private bool CanNew()=>CanBrowse() && company.HasValue && Has("Create");
    private bool CanView()=>CanBrowse() && Selected is not null;
    private bool CanEdit()=>CanView() && Selected?.Finalized==false && Has("EditDraft");
    private bool CanPrintPreview()=>CanView() && Selected is {Finalized:true,Cancelled:false};
    private bool CanFinalize()=>CanView() && Selected?.Finalized==false && Has("Finalize");
    private bool CanDeleteDraft()=>CanView() && Selected?.Finalized==false && Has("DeleteDraft");
    private bool CanCancelInvoice()=>CanView() && Selected is {Finalized:true,Cancelled:false} && Has("CancelInvoice");
    public void SetAccess(AccessSnapshot value)
    {
        if(access.EquivalentTo(value)) return;
        var changed=access.CurrentCompanyId!=value.CurrentCompanyId;
        access=value;
        if(changed || !Has("View") || !Has("ViewFinancials")) Clear();
        Notify();
    }
    public void Clear()
    {
        generation++;loaded=false;search.Cancel();details.Cancel();company=null;options=null;Rows=[];Selected=null;SelectedDetail=null;DismissFinalizationNotice();
        Notify();
    }
    public async Task OpenAsync()
    {
        Clear();
        ResetFilters();
        loaded=true;
        var version=generation;
        while(Busy && version==generation) await Task.Delay(30);
        if(version==generation) await RefreshAsync();
    }
    private void Notify()
    {
        RefreshCommand.NotifyCanExecuteChanged();NewCommand.NotifyCanExecuteChanged();ViewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();FinalizeCommand.NotifyCanExecuteChanged();
        PrintPreviewCommand.NotifyCanExecuteChanged();
        DeleteDraftCommand.NotifyCanExecuteChanged();CancelInvoiceCommand.NotifyCanExecuteChanged();
    }
    partial void OnBusyChanged(bool value)=>Notify();
    partial void OnSelectedChanged(SalesRow? value)
    {
        Notify();
        details.Cancel();
        SelectedDetail=null;
        if(!IsSplit || value is null || company is not Guid current || !Has("View") || !Has("ViewFinancials")) return;
        var version=generation;
        _=details.ScheduleAsync(false,()=>Busy,async ct=>
        {
            var result=await Send(new(SalesAction.View,current,value.Id),ct);
            if(ct.IsCancellationRequested || version!=generation || Selected?.Id!=value.Id) return;
            if(Accept(result)) SelectedDetail=result.Snapshot;
        });
    }
    partial void OnCategoryChanged(SalesCategoryFilter? value)=>Schedule(false);
    partial void OnNumberTextChanged(string? oldValue,string newValue)
    {if(MasterSearch.Query(oldValue)!=MasterSearch.Query(newValue)) Schedule(MasterSearch.Query(newValue).Length>0);}
    partial void OnCustomerTextChanged(string? oldValue,string newValue)
    {if(MasterSearch.Query(oldValue)!=MasterSearch.Query(newValue)) Schedule(MasterSearch.Query(newValue).Length>0);}
    [RelayCommand]
    private void ResetFilters()
    {
        SearchText=NumberText=CustomerText="";
        Status=Statuses[SalesInvoiceView.Rule("Filters.Status").Default switch {"Finalized"=>1,"Draft"=>2,"Cancelled"=>3,_=>0}];
        Category=Categories[SalesInvoiceView.Rule("Filters.Category").Default switch {"Local"=>1,"Interstate"=>2,_=>0}];
        Year=Customer=Series=All;
        From=To=null;
    }
    partial void OnSearchTextChanged(string? oldValue,string newValue)
    {if(MasterSearch.Query(oldValue)!=MasterSearch.Query(newValue)) Schedule(MasterSearch.Query(newValue).Length>0);}
    partial void OnStatusChanged(SalesStatusFilter? value)=>Schedule(false);
    partial void OnYearChanged(SalesChoice? value)=>Schedule(false);
    partial void OnCustomerChanged(SalesChoice? value)=>Schedule(false);
    partial void OnSeriesChanged(SalesChoice? value)=>Schedule(false);
    partial void OnFromChanged(DateTime? value)=>Schedule(false);
    partial void OnToChanged(DateTime? value)=>Schedule(false);
    private void Schedule(bool debounce)
    {if(loaded) _=search.ScheduleAsync(debounce,()=>Busy,LoadList);}
    private static Guid? Id(SalesChoice? choice)=>choice is not null && choice.Id!=Guid.Empty?choice.Id:null;
    private static DateOnly? Date(DateTime? input)=>input is DateTime date?DateOnly.FromDateTime(date):null;

    [RelayCommand(CanExecute=nameof(CanBrowse))]
    private async Task RefreshAsync()
    {
        if(!CanBrowse()) return;
        var version=generation;
        Busy=true;
        try
        {
            var response=await Send(new(SalesAction.Options));
            if(version!=generation || !Accept(response)) return;
            company=response.Company;options=response.Options;
            OnPropertyChanged(nameof(Years));OnPropertyChanged(nameof(Customers));OnPropertyChanged(nameof(SeriesChoices));
        }
        finally {Busy=false;}
        if(version==generation) await LoadList(CancellationToken.None);
    }
    private async Task LoadList(CancellationToken ct)
    {
        if(!CanBrowse() || company is null) return;
        var version=generation;
        bool Show(string id)=>SalesInvoiceView.VisibleRule("Filters."+id);
        SalesRequest Filter()=>new(SalesAction.List,company,Search:Show("Search")?MasterSearch.Query(SearchText):null,
            Finalized:Show("Status")?Status?.Finalized:null,Cancelled:Show("Status")?Status?.Cancelled:null,
            Year:Show("Year")?Id(Year):null,Series:Show("Series")?Id(Series):null,
            From:Show("From")?Date(From):null,To:Show("To")?Date(To):null,Category:Show("Category")?Category?.Value:null,
            InvoiceNumberSearch:Show("Number")?MasterSearch.Query(NumberText):null,
            CustomerSearch:Show("Customer")?MasterSearch.Query(CustomerText):null);
        var filter=Filter();
        if(filter.From>filter.To){Message="Choose a date range with From on or before To.";return;}
        Busy=true;
        try
        {
            var response=await Send(filter,ct);
            if(version!=generation || ct.IsCancellationRequested || filter!=Filter()) return;
            if(response.Outcome!=CompanyOutcome.Success && !Accept(response)) return;
            var selected=Selected?.Id;Rows=response.Rows!;Selected=Rows.FirstOrDefault(x=>x.Id==selected);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { }
        finally {Busy=false;}
    }
    [RelayCommand(CanExecute=nameof(CanNew))] private Task NewAsync()=>OpenRecord(null,false);
    [RelayCommand(CanExecute=nameof(CanView))] private Task ViewAsync()=>OpenRecord(Selected,true);
    [RelayCommand(CanExecute=nameof(CanEdit))] private Task EditAsync()=>OpenRecord(Selected,false);
    [RelayCommand(CanExecute=nameof(CanPrintPreview))]
    private async Task PrintPreviewAsync()
    {
        if(company is not Guid current || Selected is not {Finalized:true} invoice) return;
        Busy=true;
        try {await ShowPreviewAsync(current,invoice,generation);}
        finally {Busy=false;}
    }

    private async Task ShowPreviewAsync(Guid current,SalesRow invoice,int expected,Window? owner=null)
    {
        try
        {
            var result=await Send(new(SalesAction.PrintPreview,current,invoice.Id));
            if(expected!=generation || !Accept(result) || result.PrintModel is not { } model) return;
            var window=new InvoicePreviewWindow(model,Has("Print"),Has("ExportPdf"),access.Allows("InvoiceTemplate.View"),async (pdf,template)=>
            {
                if(expected!=generation) return new(CompanyOutcome.Conflict,Message:"The active company or session changed.");
                var authorized=await Send(new(pdf?SalesAction.PrintToPdf:SalesAction.Print,current,invoice.Id,PrintTemplateCode:template));
                if(authorized.Outcome==CompanyOutcome.SessionRequired) Accept(authorized);
                return authorized;
            });
            if(owner is not null) window.Owner=owner;
            window.ShowDialog();
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.IO.IOException or NotSupportedException)
        {Message="The saved invoice could not be laid out for preview.";}
    }

    internal async Task<string?> PreviewCompanyTemplateAsync(Guid companyId,string templateCode,CancellationToken ct)
    {
        if(!access.Allows("InvoiceTemplate.View",companyId)) return "You do not have template preview access.";
        var list=await Send(new(SalesAction.List,companyId,Finalized:true,Cancelled:false),ct);
        if(list.Outcome!=CompanyOutcome.Success) return Error(list);
        if(list.Rows?.FirstOrDefault() is not { } row) return "Finalize a development invoice in this company to preview the template.";
        var preview=await Send(new(SalesAction.PrintPreview,companyId,row.Id,PrintTemplateCode:templateCode),ct);
        ct.ThrowIfCancellationRequested();
        if(preview.PrintModel is not { } model) return Error(preview);
        new InvoicePreviewWindow(model,false,false,false,(_,_)=>Task.FromResult(new SalesResult(CompanyOutcome.Forbidden))).ShowDialog();
        return null;
    }
    private async Task OpenRecord(SalesRow? row,bool readOnly)
    {
        if(company is not Guid current || options is null) return;
        var version=generation;
        var savedDraft=false;
        SalesRow? finalize=null;
        Busy=true;
        try
        {
            SalesEntryDetails? entry=null;
            if(row is null)
            {
                var chooser=new MeasureEditorDialog("Select Sale Type",false)
                    {Title="Select Sale Type",Owner=System.Windows.Application.Current.MainWindow,
                        MinWidth=380,MinHeight=230,Width=440,Height=250};
                var selection=new ListView {ItemsSource=new[]{"Local Sale / Intra-State","Inter-State Sale"},
                    SelectionMode=SelectionMode.Single,MinHeight=76};
                var itemStyle=new Style(typeof(ListViewItem));
                itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
                itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
                selection.ItemContainerStyle=itemStyle;
                chooser.AddField("Select a sale type to continue",selection,null);
                var proceed=chooser.FooterButton("Continue");
                proceed.IsEnabled=false;
                proceed.IsDefault=true;
                selection.SelectionChanged+=(_,_)=>proceed.IsEnabled=selection.SelectedIndex>=0;
                void ContinueWithSelection()
                {
                    if(selection.SelectedIndex<0 || entry is not null) return;
                    entry=new(selection.SelectedIndex==0?SalesIntent.Local:SalesIntent.Interstate,"Cash");
                    chooser.Close();
                }
                proceed.Click+=(_,_)=>ContinueWithSelection();
                selection.PreviewKeyDown+=(_,args)=>
                {
                    if(args.Key!=Key.Enter || selection.SelectedIndex<0) return;
                    args.Handled=true;
                    ContinueWithSelection();
                };
                selection.MouseDoubleClick+=(_,args)=>
                {
                    if(args.ChangedButton!=MouseButton.Left || args.OriginalSource is not DependencyObject source ||
                        ItemsControl.ContainerFromElement(selection,source) is not ListViewItem {IsSelected:true}) return;
                    args.Handled=true;
                    ContinueWithSelection();
                };
                chooser.ShowDialog();
                if(entry is null) return;
            }
            var data=row is null?new SalesResult(CompanyOutcome.Success,Draft:new(DateOnly.FromDateTime(DateTime.Today),EntryDetails:entry)):
                await Send(new(SalesAction.View,current,row.Id));
            if(version!=generation || !Accept(data)) return;
            row=data.Row;
            if(readOnly && row is not null)
            {
                var view=SalesInvoiceReadView.Create(data,options);
                if(row is {Finalized:true,Cancelled:false})
                {
                    var loadingPreview=false;
                    view.Closing+=(_,args)=>args.Cancel|=loadingPreview;
                    var openPreview=view.FooterButton("Print preview / print / PDF");
                    openPreview.Click+=async (_,_) =>
                    {
                        if(loadingPreview) return;
                        loadingPreview=true;
                        openPreview.IsEnabled=false;
                        try {await ShowPreviewAsync(current,row,version,view);}
                        finally {loadingPreview=false;openPreview.IsEnabled=true;}
                    };
                }
                var edit=false;
                if(!row.Finalized && Has("EditDraft"))
                    view.FooterButton("Edit draft").Click+=(_,_)=>{edit=true;view.Close();};
                view.ShowDialog();
                if(edit && version==generation) await OpenRecord(row,false);
                return;
            }
            var dialog=new MeasureEditorDialog(SettingsDisplay.Text("Sales.Title","Sales Invoice"),false);
            if(row is null) dialog.Title=SettingsDisplay.Text("Sales.Title","Sales Invoice")+" — New draft";
            var createId=row is null?Guid.NewGuid():(Guid?)null;
            SalesInvoiceEditor.Configure(dialog,data,options,options.CompanyName??"Selected company",async (action,draft,completions,ct)=>
            {
                if(version!=generation) return new(CompanyOutcome.Conflict,Message:"Company or session changed. Close this editor.");
                var result=await Send(new(action,current,row?.Id,row?.Version??0,draft,ProductCompletions:completions,
                    CreateDraftId:action==SalesAction.Save?createId:null),ct);
                if(result.Outcome==CompanyOutcome.SessionRequired) Accept(result);
                if(action==SalesAction.Save && result.Outcome==CompanyOutcome.Success) savedDraft=true;
                return result;
            },Has("Finalize")?saved=>finalize=saved:null,
            async (customerId,ct)=>
            {
                if(version!=generation) return new(CompanyOutcome.Conflict,Message:"The company or session changed.");
                var result=await Send(new(SalesAction.CustomerAddresses,current,Customer:customerId),ct);
                if(result.Outcome==CompanyOutcome.SessionRequired) Accept(result);
                return result;
            },
            async (customerId,customerVersion,address,sharedAddress,ct)=>
            {
                if(version!=generation) return new(CustomerOutcome.CompanyChanged);
                var result=await Task.Run(()=>customers.ExecuteAsync(new(CustomerOperation.SaveInvoiceAddress,current,
                    customerId,customerVersion,Address:address,EntryId:sharedAddress),ct),ct);
                if(result.Outcome==CustomerOutcome.SessionRequired) Accept(new(CompanyOutcome.SessionRequired));
                return result;
            },
            access.Allows("Customer.View",current) && access.Allows("Customer.Edit",current));
            dialog.ShowDialog();
            if(finalize is not null && version==generation) await FinalizeRecordAsync(current,finalize,version);
        }
        finally {Busy=false;}
        if(!readOnly && version==generation)
        {
            var completedMessage=finalize is not null?Message:null;
            if(savedDraft) await RefreshAsync();
            else await LoadList(CancellationToken.None);
            if(!string.IsNullOrEmpty(completedMessage)) Message=completedMessage;
            else if(savedDraft && string.IsNullOrEmpty(Message)) Message="Sales invoice draft saved.";
        }
    }

    [RelayCommand(CanExecute=nameof(CanFinalize))]
    private async Task FinalizeAsync()
    {
        if(company is not Guid current || Selected is not {} row) return;
        Busy=true;
        var version=generation;
        try {await FinalizeRecordAsync(current,row,version);}
        finally {Busy=false;}
        if(version==generation) await LoadList(CancellationToken.None);
    }

    [RelayCommand(CanExecute=nameof(CanDeleteDraft))]
    private async Task DeleteDraftAsync()
    {
        if(company is not Guid current || Selected is not {Finalized:false} row) return;
        if(MessageBox.Show($"Permanently delete this draft for {row.Customer}?", "Delete invoice draft",
            MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
        await ChangeInvoiceStatusAsync(new(SalesAction.DeleteDraft,current,row.Id,row.Version),"Invoice draft deleted.");
    }

    [RelayCommand(CanExecute=nameof(CanCancelInvoice))]
    private async Task CancelInvoiceAsync()
    {
        if(company is not Guid current || Selected is not {Finalized:true,Cancelled:false} row) return;
        var dialog=new MeasureEditorDialog($"Cancel invoice {row.Number}",false){Height=430};
        var reason=dialog.Text("Cancellation reason *","",250);
        string? confirmedReason=null;
        dialog.Save(()=>
        {
            var text=reason.Text.Trim();
            if(text.Length==0 || text.Any(char.IsControl))
                return Task.FromResult<string?>("Enter a cancellation reason on a single line.");
            if(MessageBox.Show(dialog,$"Cancel invoice {row.Number}? Its original number and history will be retained.",
                "Confirm invoice cancellation",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)
                return Task.FromResult<string?>("Cancellation has not been applied.");
            confirmedReason=text;
            return Task.FromResult<string?>(null);
        },"Cancel invoice");
        dialog.ShowDialog();
        if(confirmedReason is not null && company==current)
            await ChangeInvoiceStatusAsync(new(SalesAction.CancelInvoice,current,row.Id,row.Version,CancellationReason:confirmedReason),
                $"Invoice {row.Number} cancelled.");
    }

    private async Task ChangeInvoiceStatusAsync(SalesRequest request,string successMessage)
    {
        var version=generation;
        Busy=true;
        try
        {
            var result=await Send(request);
            if(version!=generation) return;
            if(!Accept(result)){FinalizationNoticeKind=NoticeKind.Error;return;}
            FinalizationNoticeKind=NoticeKind.Success;
            Message=successMessage;
            _=ExpireSuccessAsync(successMessage);
        }
        finally {Busy=false;}
        if(version==generation) await LoadList(CancellationToken.None);
    }
    private async Task FinalizeRecordAsync(Guid current,SalesRow row,int version)
    {
            FinalizationNoticeKind=NoticeKind.Automatic;
            Message="Checking invoice for finalization…";
            var preview=await Send(new(SalesAction.PrepareFinalization,current,row.Id,row.Version));
            if(version!=generation) return;
            if(preview.Outcome!=CompanyOutcome.Success || preview.Snapshot is not {} snapshot || preview.Confirmation is null)
            {
                ShowFinalizationFailure(preview);
                return;
            }
            var year=Years.FirstOrDefault(y=>y.Id==snapshot.Draft.YearId)?.Label;
            var seriesName=SeriesChoices.FirstOrDefault(x=>x.Id==snapshot.Draft.SeriesId)?.Label;
            var place=GTS.Billing.Domain.CompanyRules.States.GetValueOrDefault(snapshot.Draft.PlaceOfSupply,
                snapshot.Draft.PlaceOfSupply);
            var notice=string.IsNullOrWhiteSpace(preview.Message)?"":preview.Message+Environment.NewLine+Environment.NewLine;
            var message=notice+$"{snapshot.Seller.Name}{Environment.NewLine}Customer: {snapshot.Customer.LegalName}{Environment.NewLine}"+
                $"Date: {snapshot.Draft.Date:d} · Year: {year} · Series: {seriesName}{Environment.NewLine}"+
                $"Lines: {snapshot.Lines.Count} · Taxable: {snapshot.Amounts.Taxable:N2} · GST: {snapshot.Amounts.Tax:N2}{Environment.NewLine}"+
                $"CGST: {snapshot.Amounts.Cgst:N2} · SGST: {snapshot.Amounts.Sgst:N2} · IGST: {snapshot.Amounts.Igst:N2}{Environment.NewLine}"+
                $"Grand total: INR {snapshot.Amounts.Total:N2}{Environment.NewLine}{Environment.NewLine}"+
                $"Confirm that this is a direct domestic sale of goods delivered in {place}."+
                Environment.NewLine+"Finalization issues a permanent invoice number and locks the invoice. Do you want to continue?";
            Message="";
            if(MessageBox.Show(message,"Finalize Sales Invoice?",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            Message="Finalizing Invoice…";
            var finalized=await Send(new(SalesAction.Finalize,current,row.Id,row.Version,Confirmation:preview.Confirmation));
            if(version!=generation) return;
            if(finalized.Outcome!=CompanyOutcome.Success || finalized.Row is not {Finalized:true} issued)
            { ShowFinalizationFailure(finalized);return; }
            Message=$"Sales Invoice {issued.Number} has been finalized successfully.";
            FinalizationNoticeKind=NoticeKind.Success;
            Rows=Rows.Select(item=>item.Id==issued.Id?item with {Number=issued.Number,Finalized=true,
                Taxable=issued.Taxable,Tax=issued.Tax,Total=issued.Total,Version=issued.Version}:item).ToArray();
            Selected=Rows.FirstOrDefault(item=>item.Id==issued.Id);
            _=ExpireSuccessAsync(Message);
    }

    private void ShowFinalizationFailure(SalesResult result)
    {
        var reason=Error(result);
        if(string.IsNullOrWhiteSpace(reason)) reason="The invoice did not return a finalization result. Refresh the draft and try again.";
        var details=string.Join(Environment.NewLine,(result.FieldErrors??[])
            .Select(error=>error.Line is int line?$"Line {line}: {error.Message}":error.Message)
            .Where(error=>error!=reason).Distinct());
        var summary=string.IsNullOrEmpty(details)?reason:reason+Environment.NewLine+details;
        if(result.Outcome==CompanyOutcome.SessionRequired)
        {
            MessageBox.Show(summary,"Invoice finalization stopped",MessageBoxButton.OK,MessageBoxImage.Error);
            Accept(result);
            return;
        }
        Message=summary;
        FinalizationNoticeKind=NoticeKind.Error;
    }
    private async Task ExpireSuccessAsync(string delivered)
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
        if(FinalizationNoticeKind==NoticeKind.Success && Message==delivered) DismissFinalizationNotice();
    }
    private Task<SalesResult> Send(SalesRequest request,CancellationToken ct=default)=>Task.Run(()=>service.ExecuteAsync(request,ct),ct);
    private bool Accept(SalesResult result)
    {
        if(result.Outcome==CompanyOutcome.SessionRequired) { Clear();SessionExpired?.Invoke();}
        Message=Error(result);
        return result.Outcome==CompanyOutcome.Success;
    }
    internal static string Error(SalesResult result)=>result.Message??(result.Outcome switch
    {
        CompanyOutcome.Success=>"",CompanyOutcome.Forbidden=>"Your permissions do not allow this operation.",
        CompanyOutcome.SessionRequired=>"Your session expired. Sign in again.",
        CompanyOutcome.Conflict=>"The draft changed. Refresh and reopen it.",
        CompanyOutcome.NotFound=>"The invoice is unavailable in this company.",
        CompanyOutcome.InvalidInput=>"Review the invoice details.",
        _=>"The invoice operation could not complete. Check the development database status."
    });
}
public sealed record SalesCategoryFilter(string Label,SalesIntent? Value);
public sealed record SalesStatusFilter(string Label,bool? Finalized=null,bool? Cancelled=null);
