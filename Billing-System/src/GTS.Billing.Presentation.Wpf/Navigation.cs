using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public sealed record PlannedModule(string Code, string Title)
{
    public string Status => Code is "dashboard" or "company" or "customer" or "category" or "product" or "unit" or "brand" or "hsnsac" or "tax" or "taxassignments" or "financialyear" or "documentseries" or "invoice" or "users" or "settings" ? "Available" : "Planned";
    public string Description => Status == "Available" ? Title : $"{Title} is planned. No business workflow is implemented for this module.";
    public string Caption=>Code switch
    {
        "financialyear"=>SettingsDisplay.Text("Numbering.YearTitle",Title),
        "documentseries"=>SettingsDisplay.Text("Numbering.SeriesTitle",Title),
        "invoice"=>SettingsDisplay.Text("Sales.Title","Sales Invoice"),
        _=>NavigationCaption
    };
    private string NavigationCaption=>SettingsDisplay.Text("Navigation."+(Code switch
    {
        "company"=>"CompanyMaster","customer"=>"CustomerMaster","category"=>"CategoryMaster","product"=>"ProductMaster","unit"=>"UnitMaster",
        "brand"=>"BrandMaster",
        "hsnsac"=>"HsnSacMaster","taxassignments"=>"TaxAssignments",
        "tax"=>"TaxMaster","price"=>"PriceList","payment"=>"PaymentTerms","users"=>"UserManagement",
        _=>char.ToUpperInvariant(Code[0])+Code[1..]
    }),Title);
}

public sealed record ShellMenuEntry(PlannedModule Module, IRelayCommand<PlannedModule> Navigate)
{
    public string Title => Module.Caption;
}

public sealed class ShellViewModel : ObservableObject
{
    private AccessSnapshot access=AccessSnapshot.Denied;
    private readonly ShellMenuEntry[] masters;
    private readonly ShellMenuEntry[] transactions;
    private readonly ShellMenuEntry[] administration;
    public IReadOnlyList<PlannedModule> Modules
    {
        get;
    } = new[]
    {
        new PlannedModule("dashboard", "Dashboard"), new("company", "Company Master"),
        new("customer", "Customer Master"), new("category", "Product Category Master"),
        new("product", "Product / Item Master"), new("unit", "Unit Master"), new("brand", "Brand Master"),
        new("hsnsac","HSN/SAC Master"), new("tax","GST Rate Master"), new("taxassignments","Classification rate assignments"),
        new("financialyear","Financial Year Master"), new("documentseries","Document Series Master"),
        new("price", "Price List"), new("payment", "Payment Terms"), new("quotation", "Quotation"),
        new("proforma", "Proforma Invoice"), new("invoice", "Sales Invoice"), new("retail", "Retail Invoice"),
        new("credit", "Credit Note"), new("debit", "Debit Note"), new("reports", "Reports"),
        new("users", "User Management"), new("backup", "Backup and Restore"), new("settings", "Settings")
    };
    private PlannedModule selectedModule;
    public Func<PlannedModule, bool>? NavigationGuard { get; set; }
    public PlannedModule SelectedModule
    {
        get => selectedModule;
        set
        {
            if (!Modules.Contains(value) || value == selectedModule) return;
            if (!(NavigationGuard?.Invoke(value) ?? true))
            {
                OnPropertyChanged(nameof(SelectedModule));
                return;
            }
            if(SetProperty(ref selectedModule,value))
            {
                OnPropertyChanged(nameof(CurrentHeading));OnPropertyChanged(nameof(CurrentDescription));
            }
        }
    }
    public IRelayCommand<PlannedModule> NavigateCommand
    {
        get;
    }
    public PlannedModule Dashboard => Modules[0];
    public PlannedModule Reports => Modules.Single(module => module.Code == "reports");
    public IReadOnlyList<ShellMenuEntry> MasterMenu => Visible(masters);
    public IReadOnlyList<ShellMenuEntry> TransactionMenu => Visible(transactions);
    public IReadOnlyList<ShellMenuEntry> AdministrationMenu => Visible(administration);
    public bool HasMasters=>MasterMenu.Count>0;
    public bool HasTransactions=>TransactionMenu.Count>0;
    public bool HasAdministration=>AdministrationMenu.Count>0;
    public bool CanSeeReports=>access.CanNavigate("reports");
    public bool CanNavigate(PlannedModule module)=>access.CanNavigate(module.Code);
    public string CurrentHeading=>SelectedModule.Code=="dashboard"?SettingsDisplay.Current.Get(SettingsRegistry.HomeHeading):SelectedModule.Caption;
    public string CurrentDescription=>SelectedModule.Code=="dashboard"?SettingsDisplay.Current.Get(SettingsRegistry.Welcome):SelectedModule.Description;
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(MasterMenu));OnPropertyChanged(nameof(TransactionMenu));OnPropertyChanged(nameof(AdministrationMenu));
        OnPropertyChanged(nameof(CurrentHeading));OnPropertyChanged(nameof(CurrentDescription));
    }

    public void ApplyLandingPage(string page)
    {
        var code=page switch
        {
            "Company Master"=>"company","Customer Master"=>"customer","Product Category Master"=>"category",
            "Product Master"=>"product","Settings"=>"settings",_=>"dashboard"
        };
        var target=Modules.Single(x=>x.Code==code);
        if(CanNavigate(target)) SelectedModule=target;
    }

    public void SetAccess(AccessSnapshot value)
    {
        access=value;
        OnPropertyChanged(nameof(MasterMenu));OnPropertyChanged(nameof(TransactionMenu));OnPropertyChanged(nameof(AdministrationMenu));
        OnPropertyChanged(nameof(HasMasters));OnPropertyChanged(nameof(HasTransactions));OnPropertyChanged(nameof(HasAdministration));
        OnPropertyChanged(nameof(CanSeeReports));
        if(!CanNavigate(SelectedModule))
        {
            selectedModule=Dashboard;
            OnPropertyChanged(nameof(SelectedModule));
            OnPropertyChanged(nameof(CurrentHeading));OnPropertyChanged(nameof(CurrentDescription));
        }
    }
    public string DevelopmentWarning => "UNENCRYPTED DEVELOPMENT DATABASE — synthetic data only. Invoicing, accounting and encryption remain planned.";
    public string DatabaseStatus
    {
        get;
    }
    public ShellViewModel(string databaseStatus = "No database configured. Explicit development initialization is required.")
    {
        selectedModule = Modules[0];
        DatabaseStatus = databaseStatus;
        NavigateCommand = new RelayCommand<PlannedModule>(module => { if (module is not null) SelectedModule = module; });
        masters = MenuEntries("company", "customer", "category", "product", "unit", "brand", "hsnsac", "tax", "taxassignments", "financialyear", "documentseries", "price", "payment");
        transactions = MenuEntries("quotation", "proforma", "invoice", "retail", "credit", "debit");
        administration = MenuEntries("users", "backup", "settings");
    }

    private ShellMenuEntry[] MenuEntries(params string[] codes) =>
        codes.Select(code => new ShellMenuEntry(Modules.Single(module => module.Code == code), NavigateCommand)).ToArray();
    private ShellMenuEntry[] Visible(ShellMenuEntry[] entries)=>entries.Where(x=>CanNavigate(x.Module)).ToArray();
}
