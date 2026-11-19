using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public sealed record LogoGridItem(LogoListItem Row, ImageSource? Preview);
public sealed record PresentationChoice(Guid? Id, string Name);

public sealed class InvoiceDefaultsForm(InvoiceDefaults saved)
{
    public Guid? Primary { get; set; } = saved.PrimaryLogo;
    public Guid? Secondary { get; set; } = saved.SecondaryLogo;
    public Guid? Bank { get; set; } = saved.BankId;
    public bool ShowPrimary { get; set; } = saved.ShowPrimary;
    public bool ShowSecondary { get; set; } = saved.ShowSecondary;
    public bool ShowBank { get; set; } = saved.ShowBank;
    public bool ShowUpi { get; set; } = saved.ShowUpi;
    public string? Header { get; set; } = saved.Header;
    public string? Footer { get; set; } = saved.Footer;
    public string? Signatory { get; set; } = saved.Signatory;
    public string? Terms { get; set; } = saved.Terms;
    public InvoiceDefaults Draft() => new(Primary, Secondary, Bank, ShowPrimary, ShowSecondary, ShowBank, ShowUpi,
        Header, Footer, Signatory, Terms);
}

public sealed partial class CompanyPresentationViewModel(CompanyPresentationService service) : ObservableObject, IDisposable
{
    private readonly MasterSearch taxSearch = new();
    private CompanyTaxDetail? currentTax;
    [ObservableProperty] private IReadOnlyList<CompanyTaxDetail> _taxRows = [];
    [ObservableProperty] private CompanyTaxDetail? _selectedTax;
    [ObservableProperty] private string _taxSearchText = "";
    [ObservableProperty] private string _taxGstin = "";
    [ObservableProperty] private string _taxState = "";
    [ObservableProperty] private bool _taxDefault;
    [ObservableProperty] private bool _taxOpen;
    public IReadOnlyList<CompanyChoice> TaxStates { get; } = GTS.Billing.Domain.CompanyRules.States
        .Select(x=>new CompanyChoice(x.Key,x.Value)).ToArray();
    public bool CanEnterTaxIdentity => CanWriteRecord && TaxOpen && currentTax is null;
    private readonly CancellationTokenSource closed = new();
    private readonly MasterSearch bankSearch = new();
    private readonly MasterSearch logoSearch = new();
    private InvoiceDefaults savedDefaults = new();
    private long? defaultsVersion;
    private LogoDetail? currentLogo;
    private BankDetail? currentBank;
    private byte[]? upload;

    public Guid CompanyId { get; internal set; }
    public string CompanyName { get; internal set; } = "";
    public Action? SessionExpired { get; set; }
    public Action<long>? CompanyVersionChanged { get; set; }
    public bool ReadOnly { get; init; }
    public AccessSnapshot Access { get; init; }=AccessSnapshot.Denied;
    private bool Can(string action)=>Access.Allows("Company."+action,CompanyId);
    public Func<bool>? ConfirmDiscard { get; set; }
    public Func<bool>? ConfirmRemoveLogo { get; set; }
    public IReadOnlyList<string> LogoUsages => CompanyPresentationService.LogoUsages;

    [ObservableProperty] private IReadOnlyList<LogoGridItem> _logos = [];
    [ObservableProperty] private IReadOnlyList<BankListItem> _banks = [];
    [ObservableProperty] private LogoGridItem? _selectedLogo;
    [ObservableProperty] private BankListItem? _selectedBank;
    [ObservableProperty] private IReadOnlyList<PresentationChoice> _logoChoices = [];
    [ObservableProperty] private IReadOnlyList<PresentationChoice> _bankChoices = [];
    [ObservableProperty] private InvoiceDefaultsForm _defaults = new(new());
    [ObservableProperty] private string _bankSearch = "";
    [ObservableProperty] private string _logoSearch = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _authorized;
    [ObservableProperty] private bool _logoOpen;
    [ObservableProperty] private bool _bankOpen;
    [ObservableProperty] private bool _editing;
    [ObservableProperty] private IReadOnlyList<CompanyField> _fields = [];
    [ObservableProperty] private string _usage = "";
    [ObservableProperty] private bool _active;
    [ObservableProperty] private ImageSource? _logoPreview;
    [ObservableProperty] private ImageSource? _primaryPreview;
    [ObservableProperty] private ImageSource? _secondaryPreview;
    [ObservableProperty] private string _previewCaption = "";

    public bool CanInput => Authorized && !Busy;
    public bool CanManageLogos => CanInput && !ReadOnly && Can("BrandingManage");
    public bool CanManageBanks => CanInput && !ReadOnly && Can("BankManage");
    public bool CanWriteDefaults => CanInput && !ReadOnly && Can("InvoiceSettingsManage");
    public bool CanEditDefaultBank => CanWriteDefaults && Can("BankManage");
    public bool CanPreviewDefaults => CanInput && (Can("BankView") || Can("BankManage"));
    private bool CanManageOpenRecord=>LogoOpen?CanManageLogos:BankOpen?CanManageBanks:TaxOpen&&CanWriteDefaults;
    public bool CanWriteRecord => Editing && CanManageOpenRecord;
    public bool Viewing => (LogoOpen || BankOpen || TaxOpen) && !Editing;
    public bool CanEditViewed => Viewing && CanManageOpenRecord && currentLogo?.Retired != true;
    public bool HasPendingChanges => RecordChanged || Defaults.Draft() != savedDefaults;
    private bool RecordChanged => Editing && (TaxOpen
        ? currentTax is null || TaxDraft()!=currentTax.Draft
        : LogoOpen
        ? currentLogo is not { } logo || upload is not null || LogoValues(logo.Draft.Order) != logo.Draft || !OrderMatches(logo.Draft.Order)
        : BankOpen && (currentBank is not { } bank || BankValues(bank.Draft.Order) != bank.Draft || !OrderMatches(bank.Draft.Order)));

    partial void OnBusyChanged(bool value) => NotifyState();
    partial void OnAuthorizedChanged(bool value) => NotifyState();
    partial void OnEditingChanged(bool value) => NotifyState();
    partial void OnLogoOpenChanged(bool value) => NotifyState();
    partial void OnBankOpenChanged(bool value) => NotifyState();
    partial void OnTaxOpenChanged(bool value) => NotifyState();
    partial void OnSelectedTaxChanged(CompanyTaxDetail? value) => OpenTaxCommand.NotifyCanExecuteChanged();
    partial void OnTaxSearchTextChanged(string? oldValue,string newValue)
    {
        var query=MasterSearch.Query(newValue);
        if (query==MasterSearch.Query(oldValue)) return;
        _=taxSearch.ScheduleAsync(query.Length>0,()=>Busy,ct=>RunAsync(LoadTaxesAsync,ct));
    }
    partial void OnBankSearchChanged(string? oldValue, string newValue)
    {
        var query = MasterSearch.Query(newValue);
        if (query == MasterSearch.Query(oldValue)) return;
        _ = bankSearch.ScheduleAsync(query.Length > 0, () => Busy, ct =>
            RunAsync(token => LoadBanksAsync(query, token), ct));
    }
    partial void OnLogoSearchChanged(string? oldValue, string newValue)
    {
        var query = MasterSearch.Query(newValue);
        if (query == MasterSearch.Query(oldValue)) return;
        _ = logoSearch.ScheduleAsync(query.Length > 0, () => Busy, ct => RunAsync(async token =>
        {
            var response = await SendAsync(new(PresentationAction.ListLogos, CompanyId, Search: query), token);
            if (query == MasterSearch.Query(LogoSearch) && Accept(response)) ShowLogoRows(response.Logos!);
        }, ct));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanInput));
        OnPropertyChanged(nameof(CanManageLogos));OnPropertyChanged(nameof(CanManageBanks));
        OnPropertyChanged(nameof(CanWriteDefaults));
        OnPropertyChanged(nameof(CanEditDefaultBank));OnPropertyChanged(nameof(CanPreviewDefaults));
        OnPropertyChanged(nameof(CanWriteRecord));
        OnPropertyChanged(nameof(Viewing));
        OnPropertyChanged(nameof(CanEditViewed));
        OnPropertyChanged(nameof(CanEnterTaxIdentity));
        OpenTaxCommand.NotifyCanExecuteChanged();
        OpenLogoCommand.NotifyCanExecuteChanged();
        OpenBankCommand.NotifyCanExecuteChanged();
        SetInvoiceBankCommand.NotifyCanExecuteChanged();
        ClearInvoiceBankCommand.NotifyCanExecuteChanged();
    }

    internal Task InitializeAsync() => Authorized ? Task.CompletedTask : RunAsync(LoadAllAsync);

    private async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken caller = default)
    {
        if (Busy || closed.IsCancellationRequested) return;
        Busy = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(closed.Token, caller);
        try { await work(linked.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        { Message = "Company presentation is unavailable. Close and reopen this window."; }
        finally { Busy = false; }
    }

    private async Task<PresentationResult> SendAsync(PresentationRequest request, CancellationToken ct)
    {
        var result = await Task.Run(() => service.ExecuteAsync(request, ct), ct);
        ct.ThrowIfCancellationRequested();
        if (result.Outcome == CompanyOutcome.SessionRequired) SessionExpired?.Invoke();
        if (result.Outcome == CompanyOutcome.Success && result.CompanyVersion is long version &&
            request.Action is PresentationAction.SaveLogo or PresentationAction.RemoveLogo or PresentationAction.SaveBank or
            PresentationAction.SaveDefaults or PresentationAction.SaveTax or PresentationAction.SetInvoiceBank)
            CompanyVersionChanged?.Invoke(version);
        return result;
    }

    private bool Accept(PresentationResult result)
    {
        Message = result.Message ?? result.Outcome switch
        {
            CompanyOutcome.Success => "",
            CompanyOutcome.Conflict => "The record changed or conflicts with an existing record. Reopen it before saving.",
            CompanyOutcome.SessionRequired => "Your session has expired. Sign in again.",
            CompanyOutcome.Forbidden => "This operation requires Super Admin access.",
            CompanyOutcome.NotFound => "The record is no longer available in the current company.",
            CompanyOutcome.InvalidInput => "Review the values and try again.",
            _ => "Company presentation is unavailable. Verify the approved database schema."
        };
        return result.Outcome == CompanyOutcome.Success;
    }

    private async Task LoadAllAsync(CancellationToken ct)
    {
        var logo = await SendAsync(new(PresentationAction.ListLogos, CompanyId), ct);
        if (!Accept(logo)) return;
        var bank = await SendAsync(new(PresentationAction.ListBanks, CompanyId), ct);
        if (!Accept(bank)) return;
        LogoChoices = new[] { new PresentationChoice(null, "None") }.Concat(logo.Logos!.Where(x => x.Status == "Active")
            .Select(x => new PresentationChoice(x.Id, x.Name))).ToArray();
        BankChoices = new[] { new PresentationChoice(null, "None") }.Concat(bank.Banks!.Where(x => x.Active)
            .Select(x => new PresentationChoice(x.Id, $"{x.BankName} · {x.MaskedNumber}"))).ToArray();
        var logoQuery = MasterSearch.Query(LogoSearch);
        var visible = logoQuery.Length == 0 ? logo : await SendAsync(new(PresentationAction.ListLogos, CompanyId, Search: logoQuery), ct);
        if (logoQuery == MasterSearch.Query(LogoSearch) && Accept(visible)) ShowLogoRows(visible.Logos!);
        var bankQuery = MasterSearch.Query(BankSearch);
        if (bankQuery.Length == 0) ShowBankRows(bank.Banks!);
        else await LoadBanksAsync(bankQuery, ct);
        await LoadTaxesAsync(ct);
        await LoadDefaultsAsync(ct);
    }

    private async Task LoadTaxesAsync(CancellationToken ct)
    {
        var query=MasterSearch.Query(TaxSearchText);
        var result=await SendAsync(new(PresentationAction.ListTax,CompanyId,Search:query),ct);
        if (query!=MasterSearch.Query(TaxSearchText) || !Accept(result)) return;
        var id=SelectedTax?.Id;
        TaxRows=result.TaxRows!;
        SelectedTax=TaxRows.FirstOrDefault(x=>x.Id==id);
    }

    private bool CanOpenTax() => PrepareToOpen() && SelectedTax is not null;

    [RelayCommand(CanExecute=nameof(CanOpenTax))]
    private Task OpenTaxAsync() => ReadTaxAsync(SelectedTax!.Id,false);

    [RelayCommand]
    private void AddTax()
    {
        if (CanWriteDefaults && PrepareToOpen()) ShowTax(null,true);
    }

    private Task ReadTaxAsync(Guid id,bool edit) => RunAsync(async ct=>
    {
        var result=await SendAsync(new(PresentationAction.GetTax,CompanyId,id),ct);
        if (Accept(result)) ShowTax(result.Tax,edit);
    });

    private void ShowTax(CompanyTaxDetail? detail,bool edit)
    {
        ResetEditor();
        currentTax=detail;
        TaxGstin=detail?.Draft.Gstin ?? "";
        TaxState=detail?.Draft.StateCode ?? "";
        Active=detail?.Draft.Active ?? true;
        TaxDefault=detail?.Draft.Default ?? false;
        Editing=edit;
        TaxOpen=true;
    }

    private CompanyTaxDraft TaxDraft() => new(TaxGstin,TaxState,Active,TaxDefault);

    private void ShowLogoRows(IReadOnlyList<LogoListItem> rows)
    {
        var selectedId = SelectedLogo?.Row.Id;
        Logos = rows.Select(x => new LogoGridItem(x, CompanyImageProcessor.Preview(x.Thumbnail))).ToArray();
        SelectedLogo = Logos.FirstOrDefault(x => x.Row.Id == selectedId);
    }

    private async Task LoadBanksAsync(string query, CancellationToken ct)
    {
        var response = await SendAsync(new(PresentationAction.ListBanks, CompanyId, Search: query), ct);
        if (query != MasterSearch.Query(BankSearch) || !Accept(response)) return;
        ShowBankRows(response.Banks!);
    }

    private void ShowBankRows(IReadOnlyList<BankListItem> rows)
    {
        var id = SelectedBank?.Id;
        Banks = rows;
        SelectedBank = Banks.FirstOrDefault(x => x.Id == id);
    }

    private async Task LoadDefaultsAsync(CancellationToken ct)
    {
        var response = await SendAsync(new(PresentationAction.GetDefaults, CompanyId), ct);
        Authorized = Accept(response);
        if (!Authorized) return;
        savedDefaults = response.Defaults!;
        defaultsVersion = response.CompanyVersion;
        Defaults = new(savedDefaults);
        PrimaryPreview = null;
        SecondaryPreview = null;
        PreviewCaption = "";
    }

    partial void OnSelectedLogoChanged(LogoGridItem? value) => OpenLogoCommand.NotifyCanExecuteChanged();
    partial void OnSelectedBankChanged(BankListItem? value)
    {
        OpenBankCommand.NotifyCanExecuteChanged();
        SetInvoiceBankCommand.NotifyCanExecuteChanged();
    }

    private bool CanOpenLogo() => CanInput && !LogoOpen && !BankOpen && SelectedLogo is not null;
    private bool CanOpenBank() => PrepareToOpen() && (Can("BankView") || Can("BankManage")) && SelectedBank is not null;
    private bool CanSetInvoiceBank() => CanManageBanks && CanOpenBank() && SelectedBank is { Active: true, Default: false };
    private bool CanClearInvoiceBank() => CanManageBanks && PrepareToOpen() && savedDefaults.BankId is not null;

    [RelayCommand(CanExecute = nameof(CanSetInvoiceBank))]
    private Task SetInvoiceBankAsync() => SaveBankDefaultAsync(SelectedBank?.Id);

    [RelayCommand(CanExecute = nameof(CanClearInvoiceBank))]
    private Task ClearInvoiceBankAsync() => SaveBankDefaultAsync(null);

    private Task SaveBankDefaultAsync(Guid? bankId) => RunAsync(async ct =>
    {
        if (defaultsVersion is null) return;
        var request = new PresentationRequest(PresentationAction.SetInvoiceBank,CompanyId,bankId,defaultsVersion);
        if (!Accept(await SendAsync(request, ct))) return;
        await LoadAllAsync(ct);
        if (Message.Length == 0) Message = bankId is null ? "Invoice bank default cleared." : "Invoice bank default saved.";
    });

    [RelayCommand(CanExecute = nameof(CanOpenLogo))]
    private async Task OpenLogoAsync()
    {
        if (SelectedLogo is not { } row || !PrepareToOpen()) return;
        await ReadLogoAsync(row.Row.Id, edit: false);
    }

    [RelayCommand(CanExecute = nameof(CanOpenBank))]
    private async Task OpenBankAsync()
    {
        if (SelectedBank is not { } row || !PrepareToOpen()) return;
        await ReadBankAsync(row.Id, edit: false);
    }

    [RelayCommand]
    private void AddLogo()
    {
        if (CanManageLogos && PrepareToOpen()) ShowLogo(null, true);
    }

    [RelayCommand]
    private void AddBank()
    {
        if (CanManageBanks && PrepareToOpen()) ShowBank(null, true);
    }

    [RelayCommand]
    private async Task EditRecordAsync()
    {
        if (!CanEditViewed) return;
        if (currentLogo is { } logo) await ReadLogoAsync(logo.Id, edit: true);
        if (currentBank is { } bank) await ReadBankAsync(bank.Id, edit: true);
        if (currentTax is { } tax) await ReadTaxAsync(tax.Id,edit:true);
    }

    private Task ReadLogoAsync(Guid id, bool edit) => RunAsync(async ct =>
    {
        var result = await SendAsync(new(PresentationAction.GetLogo, CompanyId, id), ct);
        if (Accept(result)) ShowLogo(result.Logo, edit && result.Logo?.Retired != true);
    });

    private Task ReadBankAsync(Guid id, bool edit) => RunAsync(async ct =>
    {
        var result = await SendAsync(new(PresentationAction.GetBank, CompanyId, id), ct);
        if (Accept(result)) ShowBank(result.Bank, edit);
    });

    private void ShowLogo(LogoDetail? detail, bool edit)
    {
        ResetEditor();
        currentLogo = detail;
        var draft = detail?.Draft ?? new("", LogoUsages[0], 0, true);
        Fields = [Field("Name", "Logo name *", draft.Name), Field("Order", "Sort order", draft.Order.ToString())];
        Usage = draft.Usage;
        Active = draft.Active;
        LogoPreview = CompanyImageProcessor.Preview(detail?.Content);
        Editing = edit;
        LogoOpen = true;
    }

    private void ShowBank(BankDetail? detail, bool edit)
    {
        ResetEditor();
        currentBank = detail;
        var draft = detail?.Draft ?? new("", "", "", "");
        Fields = [Field("Name","Bank name *",draft.BankName), Field("Holder","Account holder *",draft.AccountHolder),
            Field("Number","Account number *",draft.AccountNumber), Field("Ifsc","IFSC",draft.Ifsc),
            Field("Branch","Branch",draft.Branch), Field("Type","Account type",draft.AccountType), Field("Upi","UPI ID",draft.Upi),
            Field("Address","Bank address",draft.Address), Field("Instructions","Payment instructions",draft.Instructions),
            Field("Order","Display order",draft.Order.ToString())];
        Active = draft.Active;
        Editing = edit;
        BankOpen = true;
    }

    internal void ChooseImage(byte[] raw)
    {
        if (!CanWriteRecord || !LogoOpen) return;
        var preview = CompanyImageProcessor.Preview(raw);
        if (preview is null)
        {
            Message = "Choose a decodable PNG or JPEG up to 1 MiB and 2048 × 2048 pixels.";
            return;
        }
        upload = raw;
        LogoPreview = preview;
        Message = "Image selected. Save to store it.";
    }

    [RelayCommand]
    private Task SaveRecordAsync() => RunAsync(async ct =>
    {
        if (ReadOnly || !Editing || !(LogoOpen || BankOpen || TaxOpen)) return;
        if (TaxOpen)
        {
            var result=await SendAsync(new(PresentationAction.SaveTax,CompanyId,currentTax?.Id,currentTax?.Version,Tax:TaxDraft()),ct);
            if (!Accept(result)) return;
            ResetEditor();
            await LoadAllAsync(ct);
            if (Message.Length==0) Message="GST registration saved.";
            return;
        }
        if (!int.TryParse(Value("Order"), out var order) || order is < 0 or > 99999)
        {
            Message = "Use a whole number from 0 to 99999 for the display order.";
            return;
        }
        var request = LogoOpen
            ? new PresentationRequest(PresentationAction.SaveLogo, CompanyId, currentLogo?.Id, currentLogo?.Version,
                Logo: LogoValues(order))
            : new PresentationRequest(PresentationAction.SaveBank, CompanyId, currentBank?.Id, currentBank?.Version,
                Bank: BankValues(order));
        if (!Accept(await SendAsync(request, ct))) return;
        ResetEditor();
        await LoadAllAsync(ct);
        if (Message.Length == 0) Message = "Saved.";
    });

    [RelayCommand]
    private Task RemoveLogoAsync() => RunAsync(async ct =>
    {
        if (ReadOnly || !Editing || currentLogo is not { Retired: false } logo || ConfirmRemoveLogo?.Invoke() != true) return;
        if (!Accept(await SendAsync(new(PresentationAction.RemoveLogo, CompanyId, logo.Id, logo.Version), ct))) return;
        ResetEditor();
        await LoadAllAsync(ct);
        if (Message.Length == 0) Message = "Logo removed from use. The original image was retained.";
    });

    [RelayCommand]
    private void CloseRecord()
    {
        if (!Busy && (!RecordChanged || ConfirmDiscard?.Invoke() == true)) ResetEditor();
    }

    [RelayCommand]
    private Task SaveDefaultsAsync() => RunAsync(async ct =>
    {
        if (ReadOnly || Editing || defaultsVersion is null) return;
        var response = await SendAsync(new(PresentationAction.SaveDefaults, CompanyId, Version: defaultsVersion, Defaults: Defaults.Draft()), ct);
        if (!Accept(response)) return;
        await LoadAllAsync(ct);
        if (Message.Length == 0) Message = "Invoice defaults saved.";
    });

    [RelayCommand]
    private Task PreviewDefaultsAsync() => RunAsync(async ct =>
    {
        var response = await SendAsync(new(PresentationAction.Snapshot, CompanyId), ct);
        if (!Accept(response)) return;
        var snapshot = response.Snapshot!;
        PrimaryPreview = CompanyImageProcessor.Preview(snapshot.Primary?.Content.ToArray());
        SecondaryPreview = CompanyImageProcessor.Preview(snapshot.Secondary?.Content.ToArray());
        PreviewCaption = $"Saved defaults · Primary: {(PrimaryPreview is null ? "hidden or none" : "shown")} · " +
            $"Secondary: {(SecondaryPreview is null ? "hidden or none" : "shown")} · Bank: {snapshot.Bank?.BankName ?? "hidden or none"}" +
            $" · UPI: {(snapshot.Bank?.Upi is null ? "hidden or none" : "shown")}" +
            $" · Seller GSTIN: {snapshot.Tax?.Gstin ?? "none selected"}";
    });

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!Busy) Defaults = new(savedDefaults);
    }

    internal void DiscardEditor()
    {
        ResetEditor();
        Defaults = new(savedDefaults);
    }

    private bool PrepareToOpen() => CanInput && !LogoOpen && !BankOpen && !TaxOpen;
    private bool OrderMatches(int order) => int.TryParse(Value("Order"), out var displayed) && displayed == order;
    private LogoDraft LogoValues(int order) => new(Value("Name"), Usage, order, Active, upload);
    private BankDraft BankValues(int order)
    {
        string? Optional(string name) => Value(name) is { Length: > 0 } value ? value : null;
        return new(Value("Name"), Value("Holder"), Value("Number"), Value("Ifsc"), Optional("Branch"), Optional("Type"),
            Optional("Upi"), Optional("Address"), Optional("Instructions"), order, Active);
    }
    private string Value(string name) => Fields.Single(x => x.Name == name).Value;
    private static CompanyField Field(string name, string label, string? value) => new(name, label) { Value = value ?? "" };

    private void ResetEditor()
    {
        Editing = false;
        LogoOpen = false;
        BankOpen = false;
        TaxOpen = false;
        currentTax = null;
        currentLogo = null;
        currentBank = null;
        Fields = [];
        upload = null;
        LogoPreview = null;
    }

    public void Dispose()
    {
        bankSearch.Cancel();
        logoSearch.Cancel();
        taxSearch.Cancel();
        closed.Cancel();
        closed.Dispose();
        ResetEditor();
        SessionExpired = null;
        CompanyVersionChanged = null;
        ConfirmDiscard = null;
        ConfirmRemoveLogo = null;
    }
}
