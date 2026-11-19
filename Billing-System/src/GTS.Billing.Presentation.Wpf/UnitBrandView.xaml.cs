using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class UnitBrandView : UserControl
{
    public UnitBrandView() => InitializeComponent();
}

public sealed partial class UnitBrandViewModel(UnitBrandService service) : ObservableObject
{
    private readonly MasterSearch search = new();
    private bool brands;
    private Guid? company;
    private int generation;
    private bool loaded;
    private AccessSnapshot access = AccessSnapshot.Denied;
    public Action? SessionExpired { get; set; }
    public string Title => SettingsDisplay.Text(brands ? "Navigation.BrandMaster" : "Navigation.UnitMaster", brands ? "Brand Master" : "Unit Master");
    public IReadOnlyList<CatalogFilter> Statuses { get; } = [new("All statuses",null), new("Active",true), new("Inactive",false)];
    [ObservableProperty] private IReadOnlyList<MeasureMaster> _rows = [];
    [ObservableProperty] private MeasureMaster? _selected;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private CatalogFilter? _filter;
    [ObservableProperty] private bool _busy;

    public void RefreshDisplay() => OnPropertyChanged(nameof(Title));

    public void SetAccess(AccessSnapshot value)
    {
        if (access.EquivalentTo(value)) return;
        var companyChanged = access.CurrentCompanyId != value.CurrentCompanyId;
        access = value;
        if (companyChanged || !Can("View")) Clear();
        Notify();
    }

    public void Clear()
    {
        loaded = false;
        generation++;
        search.Cancel();
        company = null;
        Rows = [];
        Selected = null;
        Message = "";
        Notify();
    }

    public async Task OpenAsync(bool showBrands)
    {
        Clear();
        brands = showBrands;
        Search = "";
        Filter = Statuses[0];
        loaded = true;
        RefreshDisplay();
        var version = generation;
        while (Busy && version == generation) await Task.Delay(30);
        if (version != generation) return;
        await RefreshAsync();
    }

    private bool Can(string action) => access.Allows((brands ? "Brand." : "Unit.") + action);
    private bool CanBrowse() => loaded && !Busy && Can("View");
    private bool CanAdd() => CanBrowse() && company is not null && Can("Create");
    private bool CanView() => CanBrowse() && Selected is not null;
    private bool CanEdit() => CanView() && Can("Edit");
    private bool CanStatus() => CanView() && Can("StatusChange");
    private void Notify()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        ViewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        StatusCommand.NotifyCanExecuteChanged();
    }
    partial void OnBusyChanged(bool value) => Notify();
    partial void OnSelectedChanged(MeasureMaster? value) => Notify();
    partial void OnFilterChanged(CatalogFilter? value) => Schedule(false);
    partial void OnSearchChanged(string? oldValue, string newValue)
    {
        if (MasterSearch.Query(oldValue) != MasterSearch.Query(newValue)) Schedule(MasterSearch.Query(newValue).Length > 0);
    }

    private void Schedule(bool debounce)
    {
        if (loaded) _ = search.ScheduleAsync(debounce, () => Busy, RefreshCoreAsync);
    }

    [RelayCommand(CanExecute=nameof(CanBrowse))]
    private Task RefreshAsync() => RefreshCoreAsync(CancellationToken.None);

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        if (!CanBrowse()) return;
        Busy = true;
        var version = generation;
        try
        {
            var result = await SendAsync(new(MeasureAction.List, brands, Search:MasterSearch.Query(Search), Active:Filter?.Value), token);
            if (version != generation || token.IsCancellationRequested || !Accept(result)) return;
            company = result.Company;
            var selected = Selected?.Id;
            Rows = result.Rows!;
            Selected = Rows.FirstOrDefault(x => x.Id == selected);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute=nameof(CanAdd))]
    private Task AddAsync() => OpenRecordAsync(null, false);
    [RelayCommand(CanExecute=nameof(CanView))]
    private Task ViewAsync() => OpenRecordAsync(Selected, true);
    [RelayCommand(CanExecute=nameof(CanEdit))]
    private Task EditAsync() => OpenRecordAsync(Selected, false);

    private async Task OpenRecordAsync(MeasureMaster? row, bool readOnly)
    {
        var version = generation;
        if (company is not Guid current) return;
        Busy = true;
        try
        {
            if (row is not null)
            {
                var detail = await SendAsync(new(MeasureAction.View,brands,current,row.Id));
                if (version != generation || !Accept(detail)) return;
                row = detail.Rows!.Single();
            }
            var editor = new MeasureEditorDialog(Title, readOnly);
            editor.ConfigureMaster(brands, row, async (input, logo, remove) =>
            {
                if (version != generation) return "Company or session changed. Close this editor.";
                var result = await SendAsync(new(MeasureAction.Save,brands,current,row?.Id,row?.Version,Master:input,Upload:logo,RemoveLogo:remove));
                return Accept(result) ? null : Message;
            });
            editor.ShowDialog();
        }
        finally { Busy = false; }
        if (!readOnly && version == generation) await RefreshAsync();
    }

    [RelayCommand(CanExecute=nameof(CanStatus))]
    private async Task StatusAsync()
    {
        if (Selected is not { } row || company is not Guid current) return;
        if (MessageBox.Show(row.Active ? "Deactivate this record?" : "Activate this record?", Title,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Busy = true;
        bool updated;
        try { updated = Accept(await SendAsync(new(MeasureAction.Status,brands,current,row.Id,row.Version,Active:!row.Active))); }
        finally { Busy = false; }
        if (updated) await RefreshAsync();
    }

    private Task<MeasureResult> SendAsync(MeasureRequest request, CancellationToken ct = default) =>
        Task.Run(() => service.ExecuteAsync(request, ct), ct);

    private bool Accept(MeasureResult result)
    {
        if (result.Outcome == CompanyOutcome.SessionRequired) { Clear(); SessionExpired?.Invoke(); }
        Message = Error(result);
        return result.Outcome == CompanyOutcome.Success;
    }

    internal static string Error(MeasureResult result) => result.Message ?? result.Outcome switch
    {
        CompanyOutcome.Success => "",
        CompanyOutcome.Forbidden => "Your permissions do not allow this operation.",
        CompanyOutcome.SessionRequired => "Your session expired. Sign in again.",
        CompanyOutcome.Conflict => "The record changed. Refresh and try again.",
        CompanyOutcome.NotFound => "The record is unavailable in the current company.",
        CompanyOutcome.InvalidInput => "Review the entered values and try again.",
        _ => "The operation could not complete. Check the development database status."
    };
}
