using System.Windows;
using GTS.Billing.Application;
namespace GTS.Billing.Presentation.Wpf;

public partial class MainWindow : Window
{
    private readonly AuthenticationViewModel authentication;
    private readonly CancellationTokenSource lifetime = new();
    private bool closingRequested;
    private bool wasSignedIn;
    internal bool IsBillingShellVisible => BillingShell.IsVisible;
    public MainWindow(ShellViewModel viewModel, AuthenticationViewModel authentication, IDatabaseLocation location, CompanyViewModel? companies = null, CustomerViewModel? customers = null, CatalogViewModel? catalog = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        this.authentication = authentication;
        CompanyPanel.DataContext = companies;
        CustomerPanel.DataContext = customers;
        CatalogPanel.DataContext = catalog;
        if (catalog is not null) catalog.SessionExpired += async () => await authentication.CheckSessionAsync(lifetime.Token);
        viewModel.NavigationGuard = target =>
            (viewModel.SelectedModule.Code != "company" || target.Code == "company" || CompanyPanel.ConfirmDiscard()) &&
            (viewModel.SelectedModule.Code != "customer" || target.Code == "customer" || CustomerPanel.ConfirmDiscard()) &&
            (viewModel.SelectedModule.Code is not ("category" or "product") || CatalogPanel.ConfirmDiscard());
        if(customers is not null) customers.SessionExpired += async () => await authentication.CheckSessionAsync(lifetime.Token);
        viewModel.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(ShellViewModel.SelectedModule)) return;
            var isCompany = viewModel.SelectedModule.Code == "company";
            var isCustomer = viewModel.SelectedModule.Code == "customer";
            CompanyPanel.Visibility = isCompany ? Visibility.Visible : Visibility.Collapsed;
            CustomerPanel.Visibility = isCustomer ? Visibility.Visible : Visibility.Collapsed;
            var isCatalog = viewModel.SelectedModule.Code is "category" or "product";
            CatalogPanel.Visibility = isCatalog ? Visibility.Visible : Visibility.Collapsed;
            PlannedPanel.Visibility = isCompany || isCustomer || isCatalog ? Visibility.Collapsed : Visibility.Visible;
            if (!isCatalog) catalog?.Clear();
            else if (authentication.Page == AuthenticationPage.Shell && catalog is not null)
                await catalog.OpenModuleAsync(viewModel.SelectedModule.Code == "product");
            if (isCompany && authentication.Page == AuthenticationPage.Shell && companies is not null && !companies.EditorVisible)
                await companies.RefreshAsync();
            if (isCustomer && authentication.Page == AuthenticationPage.Shell && customers is not null && !customers.EditorOpen)
                await customers.RefreshAsync();
        };
        if (companies is not null)
        {
            companies.PropertyChanged += (_,e) =>
            {
                if(e.PropertyName != nameof(CompanyViewModel.CurrentCompanyId)) return;
                void ClearCompanyViews() { customers?.Clear(); catalog?.Clear(); }
                if(Dispatcher.CheckAccess()) ClearCompanyViews();else Dispatcher.BeginInvoke(new Action(ClearCompanyViews));
            };
            companies.SessionExpired += async () => await authentication.CheckSessionAsync(lifetime.Token);
            CurrentCompanyIndicator.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding(nameof(CompanyViewModel.CurrentCompany)) { Source = companies });
            authentication.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AuthenticationViewModel.Page)) return;
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                if (authentication.Page == AuthenticationPage.Shell && !wasSignedIn)
                {
                    wasSignedIn = true;
                    await companies.RefreshCurrentAsync();
                    if (viewModel.SelectedModule.Code == "company") await companies.RefreshAsync();
                    if (viewModel.SelectedModule.Code == "customer" && customers is not null) await customers.RefreshAsync();
                    if (viewModel.SelectedModule.Code is "category" or "product" && catalog is not null)
                        await catalog.OpenModuleAsync(viewModel.SelectedModule.Code == "product");
                }
                else if (authentication.Page != AuthenticationPage.Shell)
                {
                    wasSignedIn = false;
                    companies.Clear();
                    customers?.Clear();
                    catalog?.Clear();
                }
                }));
            };
        }
        DiagnosticsStatus.DataContext = authentication;
        authentication.Lifetime.ShutdownStarted += () => IsEnabled = false;
        Closing += async (_, e) =>
        {
            if (authentication.Lifetime.IsStopped) return;
            e.Cancel = true;
            if (closingRequested) return;
            if (companies?.HasUnsavedChanges == true && !companies.Busy && companies.PresentationEditor?.Busy!=true && !CompanyPanel.ConfirmDiscard()) return;
            if (customers?.Dirty == true && !customers.Busy && !CustomerPanel.ConfirmDiscard()) return;
            if (catalog?.Dirty == true && !catalog.Busy && !CatalogPanel.ConfirmDiscard()) return;
            closingRequested = true;
            IsEnabled = false;
            try { await authentication.Lifetime.ShutdownAsync(); }
            catch
            {
                MessageBox.Show("GTS Billing is waiting for active operations. Exclusive ownership is retained.",
                    "GTS Billing", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (authentication.Lifetime.IsStopped && IsLoaded) Close();
        };
        AuthenticationPanel.Attach(authentication, location, lifetime.Token);
        authentication.PropertyChanged += (_, _) => RefreshShell();
        Activated += async (_, _) => await authentication.CheckSessionAsync(lifetime.Token);
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            authentication.ForgetCodes();
        };
        RefreshShell();
    }

    private void RefreshShell()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshShell));
            return;
        }
        var signedIn = authentication.Page == AuthenticationPage.Shell && !authentication.Lifetime.IsStopping;
        BillingShell.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        BillingShell.IsEnabled = !authentication.Busy;
        AuthenticationPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        Regenerate.Visibility = authentication.CanRegenerate ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RegenerateClicked(object sender, RoutedEventArgs e)
    {
        if (!CompanyPanel.ConfirmDiscard()) return;
        if (!CustomerPanel.ConfirmDiscard()) return;
        if (!CatalogPanel.ConfirmDiscard()) return;
        await authentication.BeginRegenerationAsync(lifetime.Token);
    }
    private async void LogoutClicked(object sender, RoutedEventArgs e)
    {
        if (CompanyPanel.DataContext is CompanyViewModel { HasUnsavedChanges: true, Busy: false } company &&
            company.PresentationEditor?.Busy!=true && !CompanyPanel.ConfirmDiscard()) return;
        if (CustomerPanel.DataContext is CustomerViewModel { Dirty: true, Busy: false } && !CustomerPanel.ConfirmDiscard()) return;
        if (CatalogPanel.DataContext is CatalogViewModel { Dirty: true, Busy: false } && !CatalogPanel.ConfirmDiscard()) return;
        try { await authentication.LogoutAsync(); }
        catch
        {
            MessageBox.Show("GTS Billing could not finish logout safely. Exclusive ownership is retained.",
                "GTS Billing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
