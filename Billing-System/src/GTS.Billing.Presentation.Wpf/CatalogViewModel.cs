using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using static GTS.Billing.Presentation.Wpf.CustomerEditorFields;

namespace GTS.Billing.Presentation.Wpf;

public sealed record CatalogFilter(string Label, bool? Value);
public sealed partial class CatalogViewModel(ProductCatalogService service, UnitBrandService? measures = null,
    TaxCatalogService? tax=null) : ObservableObject
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
    private bool updatingTax;
    private Guid? originalTaxClassification;
    private string? originalTaxContext;
    private AccessSnapshot access=AccessSnapshot.Denied;
    private bool Can(string action)=>access.Allows((Products?"Product.":"Category.")+action);
    public void SetAccess(AccessSnapshot value)
    {
        if(access.EquivalentTo(value)) return;
        access=value;
        if(EditorOpen || !Can("View")) Clear();
        Notify();
    }
    private IReadOnlyList<CatalogCategory> categoryOptions = [];

    public Action? SessionExpired { get; set; }
    public Func<string,bool>? ConfirmDuplicate { get; set; }
    public bool Products => products;
    public string Title => Products ? SettingsDisplay.Text("Product.Title","Product Master") :
        SettingsDisplay.Text("Category.Title","Product Category Master");
    public void RefreshDisplay()=>OnPropertyChanged(nameof(Title));
    public string PageLabel => $"Page {page+1}";
    public bool CanBrowse => !Busy && !EditorOpen && company is not null && Can("View");
    public bool CanAdd => CanBrowse && Can("Create");
    public bool CanSave => EditorOpen && !ViewOnly && !Busy && company is not null && Can(original is null?"Create":"Edit");
    public bool CanEditViewed => ViewOnly && EditorOpen && !Busy && Can("Edit");
    public bool ShowImage => Products && EditorOpen && (!ViewOnly || SettingsDisplay.Current.Get(SettingsRegistry.ProductImage));
    public bool CanEditTax => Products && CanSave && tax is not null && access.Allows("GstRate.Assign");
    public bool CanPreviewTax => Products && EditorOpen && !Busy && TaxClassification is {Id:var id} && id!=Guid.Empty &&
        !string.IsNullOrWhiteSpace(TaxContext) && TaxDate.HasValue;
    public IReadOnlyList<string> Kinds { get; } = ["", "Goods", "Services"];
    public IReadOnlyList<CatalogFilter> Statuses { get; } = [new("All statuses",null),new("Active",true),new("Inactive",false)];

    [ObservableProperty] private IReadOnlyList<CatalogRow> _rows = [];
    [ObservableProperty] private CatalogRow? _selected;
    [ObservableProperty] private IReadOnlyList<CustomerFieldSection> _sections = [];
    [ObservableProperty] private CustomerFieldSection? _editorSection;
    [ObservableProperty] private IReadOnlyList<TaxChoice> _taxChoices=[];
    [ObservableProperty] private IReadOnlyList<string> _taxContexts=[];
    [ObservableProperty] private TaxChoice? _taxClassification;
    [ObservableProperty] private string? _taxContext;
    [ObservableProperty] private DateTime? _taxDate;
    [ObservableProperty] private string _taxMessage="";
    [ObservableProperty] private string _taxRate="";
    [ObservableProperty] private IReadOnlyList<CatalogCategory> _choices = [];
    [ObservableProperty] private IReadOnlyList<CatalogCategory> _filterChoices = [];
    [ObservableProperty] private CatalogCategory? _link;
    [ObservableProperty] private CatalogCategory? _categoryFilter;
    [ObservableProperty] private IReadOnlyList<CatalogCategory> _brandChoices = [];
    [ObservableProperty] private CatalogCategory? _brandFilter;
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
    partial void OnBrandFilterChanged(CatalogCategory? value) => QueueList(false);
    partial void OnKindFilterChanged(string value) => QueueList(false);
    partial void OnTaxClassificationChanged(TaxChoice? value)
    {
        if(updatingTax || !EditorOpen || ViewOnly) return;
        MarkDirty();
        TaxContext=null;
        TaxRate="";
        _=RunAsync(ct=>LoadTaxAsync(ct,false));
    }
    partial void OnTaxContextChanged(string? value)
    {
        if(updatingTax) return;
        MarkDirty();
        TaxRate="";
        Notify();
    }
    partial void OnTaxDateChanged(DateTime? value)
    {
        TaxRate="";
        Notify();
    }
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
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanEditViewed));
        OnPropertyChanged(nameof(ShowImage));
        OnPropertyChanged(nameof(PageLabel));
        ViewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        ChangeStatusCommand.NotifyCanExecuteChanged();
        ViewMeasuresCommand.NotifyCanExecuteChanged();
        EditMeasuresCommand.NotifyCanExecuteChanged();
        PreviewTaxCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditTax));
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
        BrandChoices=[];
        BrandFilter=null;
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
        if(response.Errors?.TryGetValue("TaxSelection",out var problem)==true)
        {
            TaxMessage=Message=problem;
            EditorSection=Sections.FirstOrDefault(x=>x.Title=="GST & Tax");
        }
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
        var brandId=BrandFilter?.Id;
        BrandChoices=[new(null,"All brands",true), ..(response.Brands ?? []).Select(x=>new CatalogCategory(x.Id,x.Label,x.Active))];
        BrandFilter=BrandChoices.FirstOrDefault(x=>x.Id==brandId) ?? BrandChoices[0];
        loading=false;
        await ListAsync(ct);
    }

    private async Task ListAsync(CancellationToken ct)
    {
        if (company is null || EditorOpen) return;
        var request=new CatalogRequest(CatalogAction.List,Products,company,Search:MasterSearch.Query(Search),
            Category:CategoryFilter?.Id, Kind:KindFilter, Active:StatusFilter?.Value, Page:page,
            PageSize:SettingsDisplay.Current.Get(SettingsRegistry.PageSize), BrandId:BrandFilter?.Id);
        var response=await Send(request,ct);
        if (request.Search != MasterSearch.Query(Search) || request.Category != CategoryFilter?.Id ||
            request.Kind != KindFilter || request.Active != StatusFilter?.Value || request.BrandId != BrandFilter?.Id) return;
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
    private bool CanViewMeasures() => Products && measures is not null && CanView();
    private bool CanEditMeasures() => CanViewMeasures() && Can("Edit");
    private string TaxKind=>Fields.FirstOrDefault(x=>x.Name=="Kind")?.Text is "Services"?"Services":"Goods";
    private Guid? TaxProductId=>original?.Product?.Kind==TaxKind ? original?.Row.Id : null;
    private Guid? SelectedTaxId=>TaxClassification is {Id:var id} && id!=Guid.Empty?id:null;

    private async Task<TaxResult> SendTaxAsync(TaxAction action,Guid? productId,TaxEntry? entry,DateOnly? date,CancellationToken ct)
    {
        if(tax is null || company is not Guid current) return new(CompanyOutcome.Unavailable);
        var response=await Task.Run(()=>tax.ExecuteAsync(new(TaxMaster.Product,action,current,productId,Entry:entry,Date:date),ct),ct);
        ct.ThrowIfCancellationRequested();
        if(response.Outcome==CompanyOutcome.SessionRequired) {Clear();SessionExpired?.Invoke();}
        return response;
    }

    private async Task LoadTaxAsync(CancellationToken ct,bool initialize)
    {
        if(!Products || tax is null) return;
        var id=TaxProductId;
        var response=await SendTaxAsync(initialize && id.HasValue?TaxAction.View:TaxAction.Options,id,
            new TaxEntry(Type:TaxKind,ClassificationId:SelectedTaxId),null,ct);
        if(response.Outcome!=CompanyOutcome.Success)
        {TaxMessage=TaxViewModel.Error(response);return;}
        if(initialize)
        {
            originalTaxClassification=response.Detail?.ClassificationId;
            originalTaxContext=response.Detail?.Context;
            if(originalTaxClassification.HasValue && Fields.FirstOrDefault(x=>x.Name=="Kind") is {} kind)
                kind.ReadOnly=true;
        }
        var selected=initialize?originalTaxClassification:SelectedTaxId;
        var priorContext=initialize?originalTaxContext:TaxContext;
        updatingTax=true;
        try
        {
            var available=response.Classifications??[];
            TaxChoices=originalTaxClassification is null?[new(Guid.Empty,"— Select HSN or SAC —"),..available]:available;
            TaxClassification=TaxChoices.FirstOrDefault(x=>x.Id==selected);
            TaxContexts=response.Contexts??[];
            TaxContext=TaxContexts.FirstOrDefault(x=>x==priorContext);
        }
        finally {updatingTax=false;}
        TaxMessage=SelectedTaxId is null?"Choose a master classification to configure this product for invoices.":
            TaxContexts.Count==0?"No context is assigned to this classification. Configure it in Classification rate assignments.":"";
        if(initialize && SelectedTaxId.HasValue && TaxContext is not null) await RefreshTaxPreviewAsync(ct);
        Notify();
    }

    [RelayCommand(CanExecute=nameof(CanPreviewTax))]
    private Task PreviewTaxAsync()=>RunAsync(RefreshTaxPreviewAsync);

    private async Task RefreshTaxPreviewAsync(CancellationToken ct)
    {
        if(TaxDate is not DateTime date) return;
        var response=await SendTaxAsync(TaxAction.Preview,TaxProductId,
            new TaxEntry(Type:TaxKind,ClassificationId:SelectedTaxId,Context:TaxContext??""),DateOnly.FromDateTime(date),ct);
        TaxRate=response.Snapshot is {} snapshot ? TaxEditor.Format(snapshot) : TaxViewModel.Error(response);
    }

    [RelayCommand(CanExecute=nameof(CanViewMeasures))]
    private Task ViewMeasuresAsync() => OpenMeasuresAsync(true);
    [RelayCommand(CanExecute=nameof(CanEditMeasures))]
    private Task EditMeasuresAsync() => OpenMeasuresAsync(false);

    private async Task OpenMeasuresAsync(bool readOnly)
    {
        if (measures is null || company is not Guid current || Selected is not { } product) return;
        await RunAsync(async ct =>
        {
            async Task<MeasureResult> SendMeasure(MeasureRequest request)
            {
                var response=await Task.Run(()=>measures.ExecuteAsync(request,ct),ct);
                ct.ThrowIfCancellationRequested();
                if (response.Outcome==CompanyOutcome.SessionRequired) { Clear(); SessionExpired?.Invoke(); }
                return response;
            }
            var data=await SendMeasure(new(MeasureAction.Product,Company:current,Id:product.Id));
            if (data.Outcome!=CompanyOutcome.Success) { Message=UnitBrandViewModel.Error(data); return; }
            var dialog=new MeasureEditorDialog(SettingsDisplay.Text("UnitBrand.ProductLinks","Units and brand")+" — "+product.Name,readOnly);
            dialog.ConfigureProduct(data,async input =>
            {
                var result=await SendMeasure(new(MeasureAction.SaveProduct,Company:current,Id:product.Id,Product:input));
                return result.Outcome==CompanyOutcome.Success?null:UnitBrandViewModel.Error(result);
            });
            dialog.ShowDialog();
            ct.ThrowIfCancellationRequested();
            if (!readOnly) await ListAsync(ct);
        });
    }
    private bool CanEdit() => Can("Edit") && (CanView() || CanEditViewed);
    private bool CanChangeStatus()=>CanView() && Can("StatusChange");
    [RelayCommand(CanExecute=nameof(CanView))] private Task ViewAsync() => OpenSelectedAsync(Selected?.Id, true);
    [RelayCommand(CanExecute=nameof(CanEdit))] private Task EditAsync() => OpenSelectedAsync(original?.Row.Id ?? Selected?.Id, false);
    [RelayCommand] private Task AddAsync()
    {
        if(!CanAdd) return Task.CompletedTask;
        return RunAsync(async ct=>{Open(null,false);await LoadTaxAsync(ct,true);});
    }
    [RelayCommand(CanExecute=nameof(CanChangeStatus))]
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
        if (Accept(result)) {Open(result.Detail,readOnly);await LoadTaxAsync(ct,true);}
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
                new("GST & Tax", [Text("Hsn","Legacy HSN / SAC"), Money("GstRate","Legacy GST rate %"), Check("TaxExempt","Legacy exemption flag"), Long("TaxNotes","Tax classification notes")]),
                new("Pricing", [Money("PurchaseRate","Default purchase rate"), Money("SalesRate","Default sales rate"), Money("Mrp","MRP"), Check("SalesInclusive","Sales price includes tax"), Check("PurchaseInclusive","Purchase price includes tax"), Text("Currency","Currency *"), Date("PricingDate","Pricing effective date")]),
                new("Inventory", [Check("StockTracked","Track stock"), Money("ReorderLevel","Reorder level"), Money("MinimumStock","Minimum stock"), Money("MaximumStock","Maximum stock"), Text("StorageLocation","Default storage location")]),
                new("Image & Details", [Long("Specifications","Specifications"), Long("Warranty","Warranty"), Long("Remarks","Internal remarks")]),
              ]
            : [new("Category", [Text("Code","Code *"), Text("Name","Name *"), Long("Description","Description"), Integer("SortOrder","Sort order"), Check("Active","Active")])];
        var defaults=SettingsDisplay.Current;
        CustomerEditorFields.Load(Fields, Products ? (object)(detail?.Product ?? new()
        {
            Kind=defaults.Get(SettingsRegistry.ProductType),QuantityPrecision=defaults.Get(SettingsRegistry.QuantityPlaces)
        }) : detail?.Category ?? new());
        foreach (var field in Fields)
        {
            field.ReadOnly=ViewOnly || field.Name is "Hsn" or "GstRate" or "TaxExempt" ||
                detail is not null && field.Name=="Code" || field.Name=="Active"&&!Can("StatusChange") ||
                Products && !Can("PricingEdit") && Sections.Any(s=>s.Title=="Pricing" && s.Fields.Contains(field));
            field.IsVisible=!Products || !readOnly || (field.Name switch
            {
                "Sku"=>defaults.Get(SettingsRegistry.ProductSku),"Barcode"=>defaults.Get(SettingsRegistry.ProductBarcode),
                "Hsn"=>defaults.Get(SettingsRegistry.ProductHsn),"Description"=>defaults.Get(SettingsRegistry.ProductDescription),_=>true
            });
            field.Changed+=changed =>
            {
                MarkDirty();
                if(changed.Name=="Kind" && !loading)
                {
                    updatingTax=true;
                    try {TaxClassification=null;TaxContext=null;TaxRate="";}
                    finally {updatingTax=false;}
                    _=RunAsync(ct=>LoadTaxAsync(ct,true));
                }
            };
        }
        EditorSection=Sections.FirstOrDefault();
        ResetTax();
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
        EditorSection=null;
        ResetTax();
        Choices=[];
        Link=null;
        Preview=null;
        upload=null;
        removeImage=false;
        Audit="";
        LinkError="";
    }

    private void ResetTax()
    {
        updatingTax=true;
        try
        {
            originalTaxClassification=null;originalTaxContext=null;
            TaxChoices=[];TaxContexts=[];TaxClassification=null;TaxContext=null;
            TaxMessage="";TaxDate=DateTime.Today;TaxRate="";
        }
        finally {updatingTax=false;}
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
                ProductTaxSelection? selection=null;
                if(SelectedTaxId is Guid taxId && (taxId!=originalTaxClassification || TaxContext!=originalTaxContext))
                {
                    if(string.IsNullOrWhiteSpace(TaxContext))
                    {
                        TaxMessage=Message="Select a tax profile context before saving.";
                        EditorSection=Sections.FirstOrDefault(x=>x.Title=="GST & Tax");
                        return;
                    }
                    selection=new(taxId,TaxContext);
                }
                request=request with { ProductDraft=input, Upload=upload, RemoveImage=removeImage,TaxSelection=selection };
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
