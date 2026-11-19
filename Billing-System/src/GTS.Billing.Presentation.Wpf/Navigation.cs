using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GTS.Billing.Presentation.Wpf;

public sealed record PlannedModule(string Code, string Title)
{
    public string Status => Code is "company" or "customer" or "category" or "product" ? "Available" : "Planned";
    public string Description => Status == "Available" ? $"{Title} · Super Admin access required." : $"{Title} is planned. No business workflow is implemented for this module.";
}

public sealed class ShellViewModel : ObservableObject
{
    public IReadOnlyList<PlannedModule> Modules
    {
        get;
    } = new[]
    {
        new PlannedModule("dashboard", "Dashboard"), new("company", "Company Master"),
        new("customer", "Customer Master"), new("category", "Product Category Master"),
        new("product", "Product / Item Master"), new("unit", "Unit Master"), new("tax", "GST / Tax Master"),
        new("price", "Price List"), new("payment", "Payment Terms"), new("quotation", "Quotation"),
        new("proforma", "Proforma Invoice"), new("invoice", "Tax Invoice"), new("retail", "Retail Invoice"),
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
            SetProperty(ref selectedModule, value);
        }
    }
    public IRelayCommand<PlannedModule> NavigateCommand
    {
        get;
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
    }
}
