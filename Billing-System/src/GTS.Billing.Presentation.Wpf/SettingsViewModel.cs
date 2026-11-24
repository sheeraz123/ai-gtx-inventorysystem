using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;
using GTS.Billing.Printing;

namespace GTS.Billing.Presentation.Wpf;

public sealed record SettingsSection(string Name)
{
    public string Caption=>Name==SalesFormConfiguration.Section?"Form & Field Configuration — Sales":Name;
}
public sealed record ConfigurationModuleChoice(string Caption,string? SettingsSection);
public sealed record ScopeChoice(SettingScope Scope,string Label);
public sealed record SettingChoice(string Value,string Label);

public sealed partial class SettingsViewModel(ApplicationSettingsService service,CompanyPresentationService presentation,
    ApplicationLifetime lifetime) : ObservableObject
{
    private readonly MasterSearch searchQueue=new();
    private AccessSnapshot access=AccessSnapshot.Denied;
    private IReadOnlyList<SettingEntry> all=[];
    private CancellationTokenSource viewLifetime=new();
    private SettingEntry? opened;
    private bool changingScope;
    private string? draftBaseline;
    private static readonly ScopeChoice[] AllScopes=
        [new(SettingScope.Application,"Application"),new(SettingScope.Company,"Current company"),new(SettingScope.User,"My preferences")];

    [ObservableProperty] private IReadOnlyList<SettingsSection> _sections=[];
    [ObservableProperty] private SettingsSection? _section;
    private const string FormsSection="Forms & Fields Configuration";
    public IReadOnlyList<ConfigurationModuleChoice> ConfigurationModules {get;}=
        [new("Menus",ConfigurationModule.Menus),..ShellViewModel.ModuleCatalog.Select(module=>
            new ConfigurationModuleChoice(module.Title,module.Code switch
            {
                "customer"=>ConfigurationModule.CustomerFields,
                "product"=>ProductFormConfiguration.Section,
                "invoice"=>SalesFormConfiguration.Section,
                _=>null
            }))];
    [ObservableProperty] private ConfigurationModuleChoice? _selectedConfigurationModule;
    [ObservableProperty] private IReadOnlyList<ScopeChoice> _scopes=[];
    [ObservableProperty] private ScopeChoice? _scope;
    [ObservableProperty] private IReadOnlyList<SettingEntry> _entries=[];
    [ObservableProperty] private SettingEntry? _selected;
    [ObservableProperty] private string _search="";
    [ObservableProperty] private IReadOnlyList<string> _groups=[];
    [ObservableProperty] private string? _group;
    [ObservableProperty] private string _value="";
    [ObservableProperty] private string _message="";
    [ObservableProperty] private string _validation="";
    [ObservableProperty] private string _previewText="";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _detailOpen;
    [ObservableProperty] private bool _editing;
    [ObservableProperty] private bool _showSecurity;
    [ObservableProperty] private CompanyPresentationViewModel? _companyPresentation;
    [ObservableProperty] private string _companyHelp="";
    [ObservableProperty] private int _presentationPage;

    public Action? SessionExpired { get; set; }
    public event EventHandler? Changed;
    public Func<string,bool>? Confirm { get; set; }
    public Func<Task>? OpenCompany { get; set; }
    public Action<string,string,byte[]?>? PreviewLogin {get;set;}
    public bool OpenedLoginImage=>opened?.Key is "Login.Logo" or "Login.Background";
    public Func<Guid,string,CancellationToken,Task<string?>>? PreviewTemplate { get; set; }
    [ObservableProperty] private string _companyLabel="";
    public bool IsOpen { get; private set; }
    public bool CompanyDetailsAvailable=>CompanyPresentation is not null;
    public bool HasCompanyHelp=>CompanyHelp.Length>0;
    public bool HasUnsavedChanges=>Editing && Value!=draftBaseline || CompanyPresentation?.HasPendingChanges==true;
    public bool IsFormsConfiguration=>Section?.Name==FormsSection;
    private string? ActiveSection=>IsFormsConfiguration?SelectedConfigurationModule?.SettingsSection:Section?.Name;
    public bool IsSalesLayoutSection=>ActiveSection==SalesFormConfiguration.Section;
    public string ResetSectionCaption=>IsFormsConfiguration?"Reset selected module to defaults":"Restore section defaults";
    public bool IsConfigurationHistory=>Section?.Name==ConfigurationModule.History;
    public bool CanList=>IsOpen && !Busy && !DetailOpen && Scope is not null && !ShowSecurity;
    public bool CanView=>CanList && Selected is not null;
    public bool CanEdit=>DetailOpen && !Editing && opened?.CanEdit==true && !Busy;
    public bool CanSave=>DetailOpen && Editing && opened?.CanEdit==true && !Busy;
    public bool CanRestore=>DetailOpen && opened?.CanEdit==true && opened.StoredText is not null && !Busy;
    public bool CanRestoreSection=>CanList && all.Any(x=>x.CanEdit && x.StoredText is not null);
    public bool IsTextSection=>Section?.Name==SettingsRegistry.LabelSection;
    public bool CanUseChoice=>opened?.Definition.Choices.Count>0;
    public bool CanUseText=>!CanUseChoice;
    public bool CanPreviewInvoice=>Scope?.Scope==SettingScope.Company && Section?.Name is
        (SettingsRegistry.Branding or SettingsRegistry.Bank or SettingsRegistry.Invoice or SettingsRegistry.Gst or SettingsRegistry.Printing);
    public bool OpenedPrinter=>opened?.Key==SettingsRegistry.Printer.Key;
    public bool OpenedInvoiceTemplate=>opened?.Key==SettingsRegistry.InvoiceTemplate.Key;
    public string SaveCaption=>OpenedInvoiceTemplate?"Save as company default":"Save";
    public string TemplateDetails=>OpenedInvoiceTemplate && InvoiceTemplates.Selectable.FirstOrDefault(t=>t.Code==Value) is { } template
        ? $"{template.Paper} · {template.Orientation} · "+(template.Code switch
        {
            InvoiceTemplates.GraftexClassic=>"Full-page ruled form with two logo positions.",
            InvoiceTemplates.Modern=>"Company branding, address panels and GST totals.",
            InvoiceTemplates.Wholesale=>"Wide line grid for SKU, brand and GST detail.",
            _=>"Compact A5 layout for smaller orders."
        }) : "";
    public string DetailTitle=>opened?.Label ?? "";
    public string DetailKey=>opened?.Key ?? "";
    public string DetailDescription=>opened?.Definition.Description ?? "";
    public string DetailScope=>opened?.Scope.ToString() ?? "";
    public string DetailDefault=>ChoiceLabel(opened?.DefaultText ?? "");
    public string RestartNote=>opened?.Definition.RestartRequired==true?"Reopen the application to apply this setting.":"";
    public string LastUpdated=>opened?.UpdatedUtc is DateTime updated?
        $"Updated {DateTime.SpecifyKind(updated,DateTimeKind.Utc).ToLocalTime():g} by {opened.UpdatedBy ?? "unknown"}":"Using a default or inherited value.";
    public IReadOnlyList<SettingChoice> Choices=>opened?.Definition.Choices.Select(value=>new SettingChoice(value,ChoiceLabel(value))).ToArray() ?? [];
    private string ChoiceLabel(string value)=>opened?.Key==SettingsRegistry.InvoiceTemplate.Key
        ? InvoiceTemplates.Selectable.FirstOrDefault(template=>template.Code==value)?.Name ?? value : value;

    partial void OnBusyChanged(bool value)=>Notify();
    partial void OnDetailOpenChanged(bool value)=>Notify();
    partial void OnEditingChanged(bool value)=>Notify();
    partial void OnSelectedChanged(SettingEntry? value)=>Notify();
    partial void OnCompanyPresentationChanged(CompanyPresentationViewModel? value)=>OnPropertyChanged(nameof(CompanyDetailsAvailable));
    partial void OnCompanyHelpChanged(string value)=>OnPropertyChanged(nameof(HasCompanyHelp));
    partial void OnShowSecurityChanged(bool value)=>Notify();
    partial void OnSearchChanged(string? oldValue,string newValue)
    {
        var text=MasterSearch.Query(newValue);
        if(text!=MasterSearch.Query(oldValue))
            _=searchQueue.ScheduleAsync(text.Length>0,()=>Busy,ct=>{ct.ThrowIfCancellationRequested();Filter();return Task.CompletedTask;});
    }
    partial void OnGroupChanged(string? value)=>Filter();

    partial void OnSectionChanged(SettingsSection? oldValue,SettingsSection? newValue)
    {
        if(changingScope || !IsOpen) return;
        if(!CanSwitch())
        {
            changingScope=true;Section=oldValue;changingScope=false;
            return;
        }
        CloseDetail();
        RebuildScopes();
        _=LoadAsync();
    }

    partial void OnScopeChanged(ScopeChoice? oldValue,ScopeChoice? newValue)
    {
        if(changingScope || !IsOpen) return;
        if(!CanSwitch())
        {
            changingScope=true;Scope=oldValue;changingScope=false;
            return;
        }
        CloseDetail();
        _=LoadAsync();
    }

    partial void OnSelectedConfigurationModuleChanged(ConfigurationModuleChoice? oldValue,ConfigurationModuleChoice? newValue)
    {
        if(changingScope || !IsOpen || !IsFormsConfiguration) return;
        if(!CanSwitch())
        {
            changingScope=true;SelectedConfigurationModule=oldValue;changingScope=false;
            return;
        }
        CloseDetail();
        RebuildScopes();
        _=LoadAsync();
    }

    public void SetAccess(AccessSnapshot value)
    {
        if(access.EquivalentTo(value)) return;
        access=value;
        if(IsOpen)
        {
            CancelPending();
            CloseDetail();
            DestroyCompany();
            RebuildSections();
            if(!Busy) _=LoadAsync();
            else _=searchQueue.ScheduleAsync(false,()=>Busy,ct=>LoadAsync(ct));
        }
    }

    public Task ActivateAsync()
    {
        IsOpen=true;
        RebuildSections();
        return LoadAsync();
    }

    public bool CanLeave()
    {
        if(!CanSwitch()) return false;
        Clear();
        return true;
    }

    public void Clear()
    {
        IsOpen=false;
        CancelPending();
        DestroyCompany();
        CloseDetail();
        all=[];Entries=[];Groups=[];Selected=null;PreviewText="";Message="";
    }

    private void CancelPending()
    {
        searchQueue.Cancel();
        var previous=viewLifetime;
        viewLifetime=new();
        previous.Cancel();
        previous.Dispose();
    }

    private bool CanSwitch()=>!Busy && CompanyPresentation?.Busy!=true &&
        (!HasUnsavedChanges || Confirm?.Invoke("Discard unsaved settings changes?")==true);

    private void RebuildSections()
    {
        changingScope=true;
        var canView=access.Allows("Settings.View");
        var options=new List<SettingsSection> {new(SettingsRegistry.General),new(SettingsRegistry.Appearance),new(SettingsRegistry.Grid),new(SettingsRegistry.Personal)};
        if(canView)
        {
            options.Insert(1,new(SettingsRegistry.LabelSection));
            if(access.CurrentCompanyId is not null)
                options.InsertRange(3,new[]{SettingsRegistry.Branding,SettingsRegistry.Bank,SettingsRegistry.Invoice,SettingsRegistry.Sales,
                    SettingsRegistry.Product,SettingsRegistry.Gst,SettingsRegistry.Printing}.Select(x=>new SettingsSection(x)));
            if(access.CurrentCompanyId is not null) options.AddRange(SalesListConfiguration.Sections.Select(x=>new SettingsSection(x)));
        }
        if(access.Allows("Settings.SecurityView")) options.Add(new(SettingsRegistry.Security));
        if(access.IsSuperAdmin) options.Add(new(LoginPage.Section));
        if(access.IsSuperAdmin && access.CurrentCompanyId is not null)
        {
            options.AddRange(new[]{ConfigurationModule.General,FormsSection,ConfigurationModule.Billing,ConfigurationModule.Shortcuts,EnvelopeConfiguration.Section,ConfigurationModule.History}
                .Select(name=>new SettingsSection(name)));
            SelectedConfigurationModule??=ConfigurationModules[0];
        }
        var previous=Section?.Name;
        Sections=options;
        Section=Sections.FirstOrDefault(x=>x.Name==previous) ?? Sections[0];
        RebuildScopes();
        changingScope=false;
    }

    private void RebuildScopes()
    {
        var section=ActiveSection;
        var previous=Scope?.Scope;
        changingScope=true;
        Scopes=AllScopes.Where(x=>
            (x.Scope==SettingScope.User || access.Allows("Settings.View")) &&
            (x.Scope!=SettingScope.Company || access.CurrentCompanyId is not null) &&
            (section==SettingsRegistry.Personal? x.Scope==SettingScope.User :
                SettingsRegistry.Definitions.Any(d=>d.Section==section && d.Scopes.HasFlag(x.Scope)))).ToArray();
        Scope=Scopes.FirstOrDefault(x=>x.Scope==previous) ??
            Scopes.FirstOrDefault(x=>x.Scope==(section==SettingsRegistry.Personal?SettingScope.User:SettingScope.Application)) ?? Scopes.FirstOrDefault();
        changingScope=false;
        Notify();
    }

    private Task LoadAsync(CancellationToken caller=default)=>Run(async ct=>
    {
        if(Section is null || !IsOpen) return;
        var name=ActiveSection;
        PreviewText="";
        all=[];Entries=[];Selected=null;Groups=[];Group=null;
        DestroyCompany();
        ShowSecurity=false;
        if(name is null)
        {
            Message="";
            PreviewText="No configurable fields are registered for this module yet.";
            return;
        }
        if(name==SettingsRegistry.Security)
        {
            var state=await Send(new(SettingsAction.Security),ct);
            if(Accept(state)) ShowSecurity=true;
            return;
        }
        if(name==ConfigurationModule.History) return;

        if(Scope is null){Message="No settings in this section are available for the current scope.";return;}
        var scope=Scope.Scope;
        var response=await Send(new(SettingsAction.List,scope,access.CurrentCompanyId,
            Section:name==SettingsRegistry.Personal?null:name),ct);
        if(!Accept(response)) return;
        all=response.Entries!;
        Groups=IsTextSection?["All modules",..all.Select(x=>x.Module).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)]:[];
        Group=Groups.FirstOrDefault();
        Filter();
        if(scope==SettingScope.Company)
        {
            SetCompanyPresentation(name);
            await OpenCompanyPresentationAsync();
        }
    },caller);

    private void SetCompanyPresentation(string section)
    {
        PresentationPage=section switch
        {
            SettingsRegistry.Branding=>1,SettingsRegistry.Bank=>2,SettingsRegistry.Invoice=>3,_=>0
        };
        if(PresentationPage==0 || access.CurrentCompanyId is not Guid company) return;
        CompanyHelp="Logos, bank accounts, bank selection, UPI, header, footer and terms use the existing Company Master records.";
        if(!access.Allows("Company.View",company)) return;
        CompanyPresentation=new(presentation)
        {
            CompanyId=company,ReadOnly=false,Access=access,
            SessionExpired=()=>SessionExpired?.Invoke(),
            ConfirmDiscard=()=>Confirm?.Invoke("Discard unsaved company presentation changes?")==true,
            ConfirmRemoveLogo=()=>Confirm?.Invoke("Retire the selected unused logo?")==true
        };
    }

    internal async Task OpenCompanyPresentationAsync()
    {
        if(CompanyPresentation is { } editor) await editor.InitializeAsync();
    }

    private void DestroyCompany()
    {
        CompanyPresentation?.Dispose();
        CompanyPresentation=null;
        CompanyHelp="";
        PresentationPage=0;
    }

    private void Filter()
    {
        var term=MasterSearch.Query(Search);
        var key=Selected?.Key;
        Entries=all.Where(x=>(Group is null or "All modules" || x.Module==Group) &&
            (term.Length==0 || x.Key.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                x.Label.Contains(term,StringComparison.OrdinalIgnoreCase) || x.EffectiveText.Contains(term,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x=>IsTextSection?x.Module:"").ThenBy(x=>x.Label,StringComparer.CurrentCultureIgnoreCase).ToArray();
        Selected=Entries.FirstOrDefault(x=>x.Key==key);
        Notify();
    }

    private void Notify()
    {
        foreach(var property in new[]{nameof(IsFormsConfiguration),nameof(IsSalesLayoutSection),nameof(ResetSectionCaption),
            nameof(IsConfigurationHistory),nameof(CanList),nameof(CanView),nameof(CanEdit),nameof(CanSave),nameof(CanRestore),
            nameof(CanRestoreSection),nameof(IsTextSection),nameof(CanUseChoice),nameof(CanUseText),nameof(CanPreviewInvoice),
            nameof(OpenedPrinter),nameof(DetailTitle),nameof(DetailKey),nameof(DetailDescription),nameof(DetailScope),
            nameof(DetailDefault),nameof(RestartNote),nameof(LastUpdated),nameof(Choices),nameof(OpenedInvoiceTemplate),
            nameof(SaveCaption),nameof(TemplateDetails),nameof(OpenedLoginImage)})
            OnPropertyChanged(property);
        ViewCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        RestoreSectionCommand.NotifyCanExecuteChanged();
    }

    private bool AllowView()=>CanView;
    private bool AllowEdit()=>CanEdit;
    private bool AllowSave()=>CanSave;
    private bool AllowRestore()=>CanRestore;
    private bool AllowRestoreSection()=>CanRestoreSection;
    partial void OnValueChanged(string value)=>OnPropertyChanged(nameof(TemplateDetails));

    [RelayCommand(CanExecute=nameof(AllowView))]
    private Task ViewAsync()=>Run(async ct=>
    {
        if(Selected is not { } row) return;
        var response=await Send(new(SettingsAction.List,row.Scope,access.CurrentCompanyId,row.Section),ct);
        if(!Accept(response)) return;
        opened=response.Entries!.SingleOrDefault(x=>x.Key==row.Key);
        if(opened is null) { Message="This setting is no longer available.";return; }
        draftBaseline=Value=opened.EffectiveText;
        Editing=false;DetailOpen=true;Validation="";PreviewText="";
        Notify();
    });

    [RelayCommand(CanExecute=nameof(AllowEdit))]
    private void Edit() { Editing=true;Validation=""; }

    [RelayCommand(CanExecute=nameof(AllowSave))]
    private async Task SaveAsync()
    {
        if(opened is not { } item) return;
        if(!item.Definition.TryNormalize(Value,out var normalized)){Validation=item.Definition.Description;return;}
        await PersistAsync(item.Scope,item.Section,[new(item.Key,normalized,item.Version)]);
    }

    [RelayCommand(CanExecute=nameof(AllowRestore))]
    private async Task RestoreAsync()
    {
        if(opened is not { } item || Confirm?.Invoke("Restore the inherited or built-in value for this setting?")!=true) return;
        await PersistAsync(item.Scope,item.Section,[new(item.Key,null,item.Version)]);
    }

    [RelayCommand(CanExecute=nameof(AllowRestoreSection))]
    private async Task RestoreSectionAsync()
    {
        if(Scope is null || ActiveSection is not { } section ||
            Confirm?.Invoke(IsFormsConfiguration?"Reset the selected module to defaults for this company?":
                "Restore all editable values in this section and scope?")!=true) return;
        var pending=all.Where(x=>x.CanEdit && x.StoredText is not null)
            .Select(x=>new SettingEdit(x.Key,null,x.Version)).ToArray();
        await PersistAsync(Scope.Scope,section==SettingsRegistry.Personal?null:section,pending);
    }

    private async Task PersistAsync(SettingScope scope,string? section,SettingEdit[] edits)
    {
        var saved=false;
        await Run(async ct=>
        {
            var response=await Send(new(SettingsAction.Save,scope,access.CurrentCompanyId,section,edits),ct);
            if(!Accept(response)){Validation=response.Message ?? "";return;}
            CloseDetail();
            saved=true;
            Changed?.Invoke(this,EventArgs.Empty);
        });
        if(!saved || !IsOpen) return;
        await LoadAsync();
        if(Message.Length==0) Message="Settings saved.";
    }

    [RelayCommand]
    private void Cancel()
    {
        if(CanSwitch()) CloseDetail();
    }

    private void CloseDetail()
    {
        Editing=false;DetailOpen=false;opened=null;Value="";draftBaseline=null;Validation="";PreviewText="";
        Notify();
    }

    [RelayCommand]
    private Task ChooseLoginImageAsync()=>Run(async ct=>
    {
        if(!Editing || !OpenedLoginImage || access.CurrentCompanyId is not Guid company)
        {
            PreviewText="Select the company that owns the uploaded image first.";
            return;
        }
        var result=await presentation.ExecuteAsync(new(PresentationAction.ListLogos,company),ct);
        if(result.Outcome!=CompanyOutcome.Success || result.Logos is null)
        {PreviewText="The selected company's branding images could not be loaded.";return;}
        var options=result.Logos.Where(x=>x.Status=="Active").ToArray();
        if(options.Length==0) {PreviewText="Upload a PNG or JPEG in Company Master before selecting it here.";return;}
        var picker=new MeasureEditorDialog("Choose login image",false);
        var selector=picker.Choice("Company image",options,options.FirstOrDefault(x=>x.Id.ToString()==Value),
            nameof(LogoListItem.Name),searchable:true);
        picker.Save(()=>
        {
            if(selector.SelectedItem is not LogoListItem selected) return Task.FromResult<string?>("Choose a company image.");
            Value=selected.Id.ToString();
            return Task.FromResult<string?>(null);
        },"Use image");
        picker.ShowDialog();
    });

    [RelayCommand]
    private Task PreviewAsync()=>Run(async ct=>
    {
        if(opened is { } item)
        {
            if(!item.Definition.TryNormalize(Value,out var normalized)){Validation=item.Definition.Description;return;}
            if(item.Section==LoginPage.Section && PreviewLogin is not null)
            {
                byte[]? image=null;
                if(OpenedLoginImage && Guid.TryParse(normalized,out var asset) && access.CurrentCompanyId is Guid owner)
                {
                    var itemResult=await presentation.ExecuteAsync(new(PresentationAction.GetLogo,owner,asset),ct);
                    image=itemResult.Logo?.Content;
                }
                PreviewLogin(item.Key,normalized,image);
                return;
            }
            if(OpenedInvoiceTemplate && access.CurrentCompanyId is Guid selected && PreviewTemplate is not null)
            {
                PreviewText=await PreviewTemplate(selected,normalized,ct)??"";
                return;
            }
            if(item.Definition.Kind==SettingKind.GridLayout)
            {
                var layout=((SettingKey<GridLayout>)item.Definition).Parse(normalized).Value;
                PreviewText=layout.Columns.Length==0?"Default columns.":string.Join(" · ",layout.Columns.Select(x=>
                    $"Column {x.Key}{(x.Hidden?" hidden":"")}{(x.Width is int width?$" ({width}px)":"")}"));
            }
            else PreviewText=normalized.Length==0?"(blank)":normalized;
            return;
        }
        if(!CanPreviewInvoice || access.CurrentCompanyId is not Guid company) return;
        var result=await Task.Run(()=>presentation.ResolveInvoicePresentationAsync(company,ct),ct);
        if(result.Outcome!=CompanyOutcome.Success || result.Snapshot?.Options is not { } invoice)
        { Message="Preview requires access to the current company's invoice presentation details.";return; }
        var bank=result.Snapshot.Bank?.BankName ?? "none shown";
        var print=invoice.Printing;
        var title=invoice.Captions.GetValueOrDefault("Invoice.Label.Title","Tax Invoice");
        PreviewText=$"{title} · {invoice.CompanyName}\n{invoice.Tagline}\nBank: {bank} · {print.PaperSize} {print.Orientation}, {print.Copies} copy/copies. Settings preview only.";
    });

    [RelayCommand]
    private Task CheckPrinterAsync()=>Run(async ct=>
    {
        if(!OpenedPrinter || string.IsNullOrWhiteSpace(Value)){PreviewText="No preferred printer is set.";return;}
        var printer=Value.Trim();
        var available=false;
        await lifetime.RunAsync(async token=>available=await Task.Run(()=>InvoicePrintPreferences.IsAvailable(printer),token),ct);
        PreviewText=available?"The selected printer is available.":"Printer unavailable. Select an installed printer before printing.";
    });

    [RelayCommand]
    private Task GoToCompanyAsync()=>OpenCompany?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private async Task OpenSalesLayoutAsync()
    {
        var changed=false;
        await Run(async ct=>
        {
            if(!IsSalesLayoutSection || access.CurrentCompanyId is not Guid company ||
                System.Windows.Application.Current?.MainWindow is not { } owner) return;
            var result=await Send(new(SettingsAction.List,SettingScope.Company,company,SalesFormConfiguration.Section),ct);
            if(!Accept(result) || result.Entries is null) return;
            var dialog=new SalesFormConfigurationDialog(owner,CompanyLabel,result.Entries,
                edits=>Send(new(SettingsAction.Save,SettingScope.Company,company,SalesFormConfiguration.Section,edits),ct),
                ()=>Send(new(SettingsAction.SalesFormHistory,SettingScope.Company,company),ct),result.GraftexSalesEntry);
            changed=dialog.ShowDialog()==true;
        });
        if(changed)
        {
            Changed?.Invoke(this,EventArgs.Empty);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task ViewConfigurationHistoryAsync()=>Run(async ct=>
    {
        if(!IsConfigurationHistory || access.CurrentCompanyId is not Guid company) return;
        var result=await Send(new(SettingsAction.ConfigurationHistory,SettingScope.Company,company),ct);
        if(!Accept(result) || result.ConfigurationHistory is not { } history) return;
        var dialog=new MeasureEditorDialog("Configuration audit history",true)
            {Owner=System.Windows.Application.Current.MainWindow};
        foreach(var entry in history)
            dialog.Text($"{entry.AtUtc:u} · {entry.Actor??"Application"} · {entry.Action} · {entry.Key}",
                entry.Changes,3000,true);
        if(history.Count==0) dialog.ShowMessage("No configuration changes have been recorded for the selected company.");
        dialog.ShowDialog();
    });

    [RelayCommand]
    private async Task CopyConfigurationAsync()
    {
        var changed=false;
        await Run(async ct=>
        {
            if(!IsConfigurationHistory || access.CurrentCompanyId is not Guid company) return;
            var result=await Send(new(SettingsAction.ConfigurationSources,SettingScope.Company,company),ct);
            if(!Accept(result) || result.ConfigurationSources is not { Count:>0 } sources)
            {if(result.Outcome==CompanyOutcome.Success) Message="No other active company is available.";return;}
            var dialog=new MeasureEditorDialog("Copy configuration to "+CompanyLabel,false)
                {Owner=System.Windows.Application.Current.MainWindow};
            var selector=dialog.Choice<ConfigurationSource>("Source company",sources,null,nameof(ConfigurationSource.Name));
            dialog.ShowMessage("Copies saved general, form, menu and billing overrides. Matching destination settings will be replaced.");
            dialog.Save(async ()=>
            {
                if(selector.SelectedItem is not ConfigurationSource selected) return "Select a source company.";
                if(System.Windows.MessageBox.Show(dialog,$"Copy saved settings from {selected.Name} to the selected company?",
                    "Confirm configuration copy",System.Windows.MessageBoxButton.YesNo,System.Windows.MessageBoxImage.Question,
                    System.Windows.MessageBoxResult.No)!=System.Windows.MessageBoxResult.Yes) return "Copy canceled.";
                var copied=await Send(new(SettingsAction.CopyConfiguration,SettingScope.Company,company,SourceCompanyId:selected.Id),ct);
                changed=Accept(copied);
                return changed?null:Message;
            },"Copy configuration");
            dialog.ShowDialog();
        });
        if(changed) Changed?.Invoke(this,EventArgs.Empty);
    }

    private async Task Run(Func<CancellationToken,Task> action,CancellationToken caller=default)
    {
        if(Busy || lifetime.IsStopping) return;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(caller,viewLifetime.Token);
        Busy=true;
        try { await action(linked.Token); }
        catch(OperationCanceledException) { }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.IO.IOException)
        { Message="Settings could not be loaded or saved."; }
        finally { Busy=false; }
    }

    private async Task<SettingsResult> Send(SettingsRequest request,CancellationToken ct)
    {
        var response=await Task.Run(()=>service.ExecuteAsync(request,ct),ct);
        ct.ThrowIfCancellationRequested();
        if(response.Outcome==CompanyOutcome.SessionRequired){Clear();SessionExpired?.Invoke();}
        return response;
    }

    private bool Accept(SettingsResult response)
    {
        Message=response.Message ?? response.Outcome switch
        {
            CompanyOutcome.Success=>"",
            CompanyOutcome.Forbidden=>"The current account does not have access to these settings.",
            CompanyOutcome.SessionRequired=>"Sign in again to continue.",
            CompanyOutcome.Conflict=>"Settings or company scope changed. Reopen this section before saving.",
            CompanyOutcome.InvalidInput=>"Check the setting value and try again.",
            _=>"Settings are unavailable."
        };
        return response.Outcome==CompanyOutcome.Success;
    }
}
