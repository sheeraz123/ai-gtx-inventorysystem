using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class SalesInvoiceView : UserControl
{
    public SalesInvoiceView()=>InitializeComponent();
}

public sealed partial class SalesInvoiceViewModel(SalesInvoiceService service):ObservableObject
{
    private readonly MasterSearch search=new();
    private AccessSnapshot access=AccessSnapshot.Denied;
    private SalesOptions? options;
    private Guid? company;
    private int generation;
    private bool loaded;
    public Action? SessionExpired {get;set;}
    public IReadOnlyList<CatalogFilter> Statuses {get;}=[new("All statuses",null),new("Finalized",true),new("Draft",false)];
    private static readonly SalesChoice All=new(Guid.Empty,"All");
    public IReadOnlyList<SalesChoice> Years=>[All,..options?.Years??[]];
    public IReadOnlyList<SalesChoice> Customers=>[All,..options?.Customers??[]];
    public IReadOnlyList<SalesChoice> SeriesChoices=>[All,..options?.Series??[]];
    [ObservableProperty] private IReadOnlyList<SalesRow> _rows=[];
    [ObservableProperty] private SalesRow? _selected;
    [ObservableProperty] private string _searchText="";
    [ObservableProperty] private string _message="";
    [ObservableProperty] private CatalogFilter? _status;
    [ObservableProperty] private SalesChoice? _year;
    [ObservableProperty] private SalesChoice? _customer;
    [ObservableProperty] private SalesChoice? _series;
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;
    [ObservableProperty] private bool _busy;

    private bool Has(string action)=>access.Allows("SalesInvoice."+action);
    private bool CanBrowse()=>loaded && !Busy && Has("View") && Has("ViewFinancials");
    private bool CanNew()=>CanBrowse() && company.HasValue && Has("Create");
    private bool CanView()=>CanBrowse() && Selected is not null;
    private bool CanEdit()=>CanView() && Selected?.Finalized==false && Has("EditDraft");
    private bool CanPrintPreview()=>CanView() && Selected?.Finalized==true;
    private bool CanFinalize()=>CanView() && Selected?.Finalized==false && Has("Finalize");
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
        generation++;loaded=false;search.Cancel();company=null;options=null;Rows=[];Selected=null;Message="";
        Notify();
    }
    public async Task OpenAsync()
    {
        Clear();
        SearchText="";Status=Statuses[0];Year=Customer=Series=All;From=To=null;loaded=true;
        var version=generation;
        while(Busy && version==generation) await Task.Delay(30);
        if(version==generation) await RefreshAsync();
    }
    private void Notify()
    {
        RefreshCommand.NotifyCanExecuteChanged();NewCommand.NotifyCanExecuteChanged();ViewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();FinalizeCommand.NotifyCanExecuteChanged();
        PrintPreviewCommand.NotifyCanExecuteChanged();
    }
    partial void OnBusyChanged(bool value)=>Notify();
    partial void OnSelectedChanged(SalesRow? value)=>Notify();
    partial void OnSearchTextChanged(string? oldValue,string newValue)
    {if(MasterSearch.Query(oldValue)!=MasterSearch.Query(newValue)) Schedule(MasterSearch.Query(newValue).Length>0);}
    partial void OnStatusChanged(CatalogFilter? value)=>Schedule(false);
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
        SalesRequest Filter()=>new(SalesAction.List,company,Search:MasterSearch.Query(SearchText),Finalized:Status?.Value,
            Year:Id(Year),Customer:Id(Customer),Series:Id(Series),From:Date(From),To:Date(To));
        var filter=Filter();
        if(filter.From>filter.To){Message="Choose a date range with From on or before To.";return;}
        Busy=true;
        try
        {
            var response=await Send(filter,ct);
            if(version!=generation || ct.IsCancellationRequested || filter!=Filter() || !Accept(response)) return;
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
        var list=await Send(new(SalesAction.List,companyId,Finalized:true),ct);
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
        Busy=true;
        try
        {
            var data=row is null?new SalesResult(CompanyOutcome.Success,Draft:new(DateOnly.FromDateTime(DateTime.Today))):
                await Send(new(SalesAction.View,current,row.Id));
            if(version!=generation || !Accept(data)) return;
            row=data.Row;
            if(readOnly && row is not null)
            {
                var view=SalesInvoiceReadView.Create(data,options);
                if(row.Finalized)
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
            SalesRow? finalize=null;
            SalesInvoiceEditor.Configure(dialog,data,options,options.CompanyName??"Selected company",async (action,draft,completions,ct)=>
            {
                if(version!=generation) return new(CompanyOutcome.Conflict,Message:"Company or session changed. Close this editor.");
                var result=await Send(new(action,current,row?.Id,row?.Version??0,draft,ProductCompletions:completions,
                    CreateDraftId:action==SalesAction.Save?createId:null),ct);
                if(result.Outcome==CompanyOutcome.SessionRequired) Accept(result);
                if(action==SalesAction.Save && result.Outcome==CompanyOutcome.Success) savedDraft=true;
                return result;
            },Has("Finalize")?saved=>finalize=saved:null);
            dialog.ShowDialog();
            if(finalize is not null && version==generation) await FinalizeRecordAsync(current,finalize,version);
        }
        finally {Busy=false;}
        if(!readOnly && version==generation)
        {
            var completedMessage=Message.StartsWith("Finalized invoice ",StringComparison.Ordinal)?Message:null;
            if(savedDraft) await RefreshAsync();
            else await LoadList(CancellationToken.None);
            if(savedDraft && string.IsNullOrEmpty(Message)) Message=completedMessage??"Sales invoice draft saved.";
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

    private async Task FinalizeRecordAsync(Guid current,SalesRow row,int version)
    {
        {
            var result=await Send(new(SalesAction.View,current,row.Id));
            if(version!=generation || !Accept(result) || result.Row is not {Finalized:false} persisted) return;
            var preview=await Send(new(SalesAction.Preview,current,row.Id,persisted.Version,result.Draft));
            if(version!=generation || !Accept(preview) || preview.Snapshot is not {} snapshot || preview.Confirmation is null) return;
            var year=Years.FirstOrDefault(y=>y.Id==snapshot.Draft.YearId)?.Label;
            var seriesName=SeriesChoices.FirstOrDefault(x=>x.Id==snapshot.Draft.SeriesId)?.Label;
            var message=$"{snapshot.Seller.Name}{Environment.NewLine}Customer: {snapshot.Customer.LegalName}{Environment.NewLine}"+
                $"Date: {snapshot.Draft.Date:d} · Year: {year} · Series: {seriesName}{Environment.NewLine}"+
                $"Lines: {snapshot.Lines.Count} · Taxable: {snapshot.Amounts.Taxable:N2} · GST: {snapshot.Amounts.Tax:N2}{Environment.NewLine}"+
                $"Grand total: INR {snapshot.Amounts.Total:N2}{Environment.NewLine}{Environment.NewLine}Finalize and issue the permanent invoice number?";
            if(MessageBox.Show(message,"Finalize draft",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            var finalized=await Send(new(SalesAction.Finalize,current,row.Id,persisted.Version,Confirmation:preview.Confirmation));
            if(!Accept(finalized)) return;
            Message="Finalized invoice "+finalized.Row?.Number;
        }
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
