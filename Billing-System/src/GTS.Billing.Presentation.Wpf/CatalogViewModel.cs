using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using static GTS.Billing.Presentation.Wpf.CustomerEditorFields;

namespace GTS.Billing.Presentation.Wpf;

public sealed record CatalogFilter(string Label, bool? Value);
public sealed partial class CatalogViewModel(ProductCatalogService service) : ObservableObject
{
    private readonly MasterSearch liveSearch = new();
    private CancellationTokenSource contextLifetime = new();
    private Guid? company;
    private int page;
    private bool more;
    private bool loading;
    private byte[]? upload;
    private bool removeImage;
    private bool products;
    private CatalogDetail? original;
    private IReadOnlyList<CatalogCategory> categoryOptions = [];

    public Action? SessionExpired { get; set; }
    public Func<string,bool>? ConfirmDuplicate { get; set; }
    public bool Products => products;
    public string Title => Products ? "Product Master" : "Product Category Master";
    public string PageLabel => $"Page {page+1}";
    public bool CanBrowse => !Busy && !EditorOpen && company is not null;
    public bool CanSave => EditorOpen && !ViewOnly && !Busy && company is not null;
    public bool CanEditViewed => ViewOnly && EditorOpen && !Busy;
    public bool ShowImage => Products && EditorOpen;
    public IReadOnlyList<string> Kinds { get; } = ["", "Goods", "Services"];
    public IReadOnlyList<CatalogFilter> Statuses { get; } = [new("All statuses",null),new("Active",true),new("Inactive",false)];

    [ObservableProperty] private IReadOnlyList<CatalogRow> _rows = [];
    [ObservableProperty] private CatalogRow? _selected;
    [ObservableProperty] private IReadOnlyList<CustomerFieldSection> _sections = [];
    [ObservableProperty] private IReadOnlyList<CatalogCategory> _choices = [];
    [ObservableProperty] private IReadOnlyList<CatalogCategory> _filterChoices = [];
    [ObservableProperty] private CatalogCategory? _link;
    [ObservableProperty] private CatalogCategory? _categoryFilter;
    [ObservableProperty] private CatalogFilter? _statusFilter;
    [ObservableProperty] private string _kindFilter = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _audit = "";
    [ObservableProperty] private string _linkError = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _viewOnly;
    [ObservableProperty] private bool _editorOpen;
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private ImageSource? _preview;

    private IEnumerable<CustomerField> Fields => Sections.SelectMany(x => x.Fields);
    partial void OnBusyChanged(bool value) => Notify();
    partial void OnSelectedChanged(CatalogRow? value) => Notify();
    partial void OnEditorOpenChanged(bool value) => Notify();
    partial void OnViewOnlyChanged(bool value) => Notify();
    partial void OnLinkChanged(CatalogCategory? value) => MarkDirty();
    partial void OnStatusFilterChanged(CatalogFilter? value) => QueueList(false);
    partial void OnCategoryFilterChanged(CatalogCategory? value) => QueueList(false);
    partial void OnKindFilterChanged(string value) => QueueList(false);
    partial void OnSearchChanged(string? oldValue, string newValue)
    {
        var query = MasterSearch.Query(newValue);
        if (query != MasterSearch.Query(oldValue)) QueueList(query.Length > 0);
    }

    private void MarkDirty()
    {
        if (!loading && EditorOpen && !ViewOnly) Dirty=true;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanEditViewed));
        OnPropertyChanged(nameof(ShowImage));
        OnPropertyChanged(nameof(PageLabel));
        ViewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        ChangeStatusCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
    }

    public void Clear()
    {
        liveSearch.Cancel();
        var previous = contextLifetime;
        contextLifetime = new();
        previous.Cancel();
        previous.Dispose();
        company = null;
        Cancel();
        Rows=[];
        Selected=null;
        categoryOptions=[];
        FilterChoices=[];
        Message="";
        Notify();
    }

    public Task OpenModuleAsync(bool showProducts)
    {
        Clear();
        products=showProducts;
        OnPropertyChanged(nameof(Products));
        OnPropertyChanged(nameof(Title));
        Search="";
        StatusFilter=Statuses[0];
        KindFilter="";
        return RefreshAsync();
    }

    private void QueueList(bool debounce)
    {
        if (loading || company is null || EditorOpen) return;
        _ = liveSearch.ScheduleAsync(debounce, () => Busy, ct => RunAsync(async token =>
        {
            page=0;
            await ListAsync(token);
        }, ct));
    }

    private async Task RunAsync(Func<CancellationToken,Task> work, CancellationToken ct = default)
    {
        if (Busy) return;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct, contextLifetime.Token);
        Busy=true;
        try { await work(linked.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.IO.IOException)
        { Message="The catalog operation could not complete. Refresh and try again."; }
        finally { Busy=false; }
    }

    private async Task<CatalogResult> Send(CatalogRequest request, CancellationToken ct)
    {
        var response=await Task.Run(() => service.ExecuteAsync(request,ct),ct);
        ct.ThrowIfCancellationRequested();
        if (response.Outcome == CompanyOutcome.SessionRequired) { Clear(); SessionExpired?.Invoke(); }
        return response;
    }

    private bool Accept(CatalogResult response)
    {
        Message=response.Message ?? response.Outcome switch
        {
            CompanyOutcome.Success => "",
            CompanyOutcome.SessionRequired => "Your session expired. Sign in again.",
            CompanyOutcome.InvalidInput => "Review the highlighted values.",
            CompanyOutcome.NotFound => "The record is unavailable in the current company.",
            CompanyOutcome.Conflict => "The record changed or conflicts with existing data. Refresh before editing.",
            CompanyOutcome.Forbidden => "This module requires Super Admin access.",
            _ => "The catalog is unavailable. Verify the approved database schema."
        };
        foreach (var field in Fields) field.Error=response.Errors?.GetValueOrDefault(field.Name) ?? "";
        LinkError=response.Errors?.GetValueOrDefault(Products ? "CategoryId" : "ParentId") ?? "";
        if (response.Errors?.TryGetValue("Image", out var image) == true) Message=image;
        return response.Outcome == CompanyOutcome.Success;
    }

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(async ct =>
    {
        page=0;
        await ReloadContext(ct);
    });

    private async Task ReloadContext(CancellationToken ct)
    {
        var response=await Send(new(CatalogAction.Context,Products),ct);
        if (!Accept(response)) { company=null; Rows=[]; Selected=null; return; }
        company=response.Company;
        categoryOptions=response.Categories!;
        loading=true;
        var filterId=CategoryFilter?.Id;
        FilterChoices=[new(null,"All categories",true), ..categoryOptions];
        CategoryFilter=FilterChoices.FirstOrDefault(x => x.Id == filterId) ?? FilterChoices[0];
        loading=false;
        await ListAsync(ct);
    }

    private async Task ListAsync(CancellationToken ct)
    {
        if (company is null || EditorOpen) return;
        var request=new CatalogRequest(CatalogAction.List,Products,company,Search:MasterSearch.Query(Search),
            Category:CategoryFilter?.Id, Kind:KindFilter, Active:StatusFilter?.Value, Page:page);
        var response=await Send(request,ct);
        if (request.Search != MasterSearch.Query(Search) || request.Category != CategoryFilter?.Id ||
            request.Kind != KindFilter || request.Active != StatusFilter?.Value) return;
        if (!Accept(response)) { Rows=[]; Selected=null; more=false; Notify(); return; }
        if (page > 0 && response.Rows!.Count == 0)
        {
            page--;
            response=await Send(request with { Page=page },ct);
            if (!Accept(response)) return;
        }
        var id=Selected?.Id;
        Rows=response.Rows!;
        Selected=Rows.FirstOrDefault(x => x.Id == id);
        more=response.More;
        Notify();
    }

    private bool CanNext() => CanBrowse && more;
    private bool CanPrevious() => CanBrowse && page>0;
    [RelayCommand(CanExecute=nameof(CanNext))] private Task NextAsync() => RunAsync(async ct => { page++; await ListAsync(ct); });
    [RelayCommand(CanExecute=nameof(CanPrevious))] private Task PreviousAsync() => RunAsync(async ct => { page--; await ListAsync(ct); });

    private bool CanView() => CanBrowse && Selected is not null;
    private bool CanEdit() => CanView() || CanEditViewed;
    [RelayCommand(CanExecute=nameof(CanView))] private Task ViewAsync() => OpenSelectedAsync(Selected?.Id, true);
    [RelayCommand(CanExecute=nameof(CanEdit))] private Task EditAsync() => OpenSelectedAsync(original?.Row.Id ?? Selected?.Id, false);
    [RelayCommand] private void Add() { if (CanBrowse) Open(null,false); }
    [RelayCommand(CanExecute=nameof(CanView))]
    private Task ChangeStatusAsync() => RunAsync(async ct =>
    {
        if (Selected is not { } row) return;
        var result=await Send(new(CatalogAction.Status,Products,company,row.Id,row.Version,Active:!row.Active),ct);
        if (Accept(result)) await ReloadContext(ct);
    });

    private Task OpenSelectedAsync(Guid? id, bool readOnly) => RunAsync(async ct =>
    {
        if (id is null) return;
        var result=await Send(new(CatalogAction.Get,Products,company,id),ct);
        if (Accept(result)) Open(result.Detail,readOnly);
    });

    private void Open(CatalogDetail? detail, bool readOnly)
    {
        liveSearch.Cancel();
        loading=true;
        original=detail;
        upload=null;
        removeImage=false;
        Preview=CompanyImageProcessor.Preview(detail?.Image);
        ViewOnly=readOnly;
        var currentLink=Products ? detail?.Product?.CategoryId : detail?.Category?.ParentId;
        var candidates=categoryOptions.Where(x => x.Id == currentLink || x.Active && (Products || x.Id != detail?.Row.Id)).ToArray();
        Choices=Products ? candidates : [new(null,"Root category",true), ..candidates];
        Link=Choices.FirstOrDefault(x => x.Id == currentLink);
        Sections=Products
            ? [
                new("General", [Text("Code","Code *"), Text("Name","Name *"), Text("ShortName","Short name"), Long("Description","Description"), Choice("Kind","Product type *",["Goods","Services"]), Check("Active","Active")]),
                new("Identification", [Text("Brand","Brand"), Text("Sku","SKU"), Text("Barcode","Barcode"), Text("PartNumber","Manufacturer part number"), Text("ModelNumber","Model number")]),
                new("Units", [Text("BaseUnit","Base unit *"), Text("SalesUnit","Sales unit"), Money("SalesFactor","Base units per sales unit"), Text("PurchaseUnit","Purchase unit"), Money("PurchaseFactor","Base units per purchase unit"), Integer("QuantityPrecision","Quantity decimal places")]),
                new("GST & Tax", [Text("Hsn","HSN / SAC"), Money("GstRate","Provisional GST rate %"), Check("TaxExempt","Exemption flag"), Long("TaxNotes","Tax classification notes")]),
                new("Pricing", [Money("PurchaseRate","Default purchase rate"), Money("SalesRate","Default sales rate"), Money("Mrp","MRP"), Check("SalesInclusive","Sales price includes tax"), Check("PurchaseInclusive","Purchase price includes tax"), Text("Currency","Currency *"), Date("PricingDate","Pricing effective date")]),
                new("Inventory", [Check("StockTracked","Track stock"), Money("ReorderLevel","Reorder level"), Money("MinimumStock","Minimum stock"), Money("MaximumStock","Maximum stock"), Text("StorageLocation","Default storage location")]),
                new("Image & Details", [Long("Specifications","Specifications"), Long("Warranty","Warranty"), Long("Remarks","Internal remarks")]),
              ]
            : [new("Category", [Text("Code","Code *"), Text("Name","Name *"), Long("Description","Description"), Integer("SortOrder","Sort order"), Check("Active","Active")])];
        CustomerEditorFields.Load(Fields, Products ? (object)(detail?.Product ?? new()) : detail?.Category ?? new());
        foreach (var field in Fields)
        {
            field.ReadOnly=detail is not null && field.Name=="Code";
            field.Changed+=_ => MarkDirty();
        }
        Audit=detail is null ? "Audit details are recorded when this record is saved." :
            $"Created {detail.CreatedUtc:u} by {detail.CreatedBy} · Updated {detail.UpdatedUtc:u} by {detail.UpdatedBy}";
        Message="";
        LinkError="";
        Dirty=false;
        EditorOpen=true;
        loading=false;
    }

    public void Cancel()
    {
        liveSearch.Cancel();
        EditorOpen=false;
        Dirty=false;
        ViewOnly=false;
        original=null;
        Sections=[];
        Choices=[];
        Link=null;
        Preview=null;
        upload=null;
        removeImage=false;
        Audit="";
        LinkError="";
    }

    public void SetImage(byte[]? bytes)
    {
        if (!CanSave || !Products) return;
        var image=CompanyImageProcessor.Preview(bytes);
        if (bytes is not null && image is null) { Message="Choose a valid PNG or JPEG up to 1 MiB and 2048 × 2048 pixels."; return; }
        upload=bytes;
        removeImage=bytes is null;
        Preview=image;
        Dirty=true;
    }

    [RelayCommand]
    private Task SaveAsync()
    {
        if (!CanSave) return Task.CompletedTask;
        return RunAsync(async ct =>
        {
            var errors=new Dictionary<string,string>();
            var request=new CatalogRequest(CatalogAction.Save,Products,company,original?.Row.Id,original?.Row.Version);
            if (Products)
            {
                var input=CustomerEditorFields.Read<ProductDraft>(Fields,"",errors) with { CategoryId=Link?.Id ?? Guid.Empty };
                request=request with { ProductDraft=input, Upload=upload, RemoveImage=removeImage };
            }
            else request=request with { CategoryDraft=CustomerEditorFields.Read<CategoryDraft>(Fields,"",errors) with { ParentId=Link?.Id } };
            if (errors.Count!=0) { Accept(new(CompanyOutcome.InvalidInput,Errors:errors)); return; }
            var response=await Send(request,ct);
            if (response.DuplicateWarning)
            {
                if (ConfirmDuplicate?.Invoke(response.Message!) != true) return;
                response=await Send(request with { AcceptDuplicates=true },ct);
            }
            if (!Accept(response)) return;
            Cancel();
            await ReloadContext(ct);
            if (Message.Length==0) Message="Saved.";
        });
    }
}
