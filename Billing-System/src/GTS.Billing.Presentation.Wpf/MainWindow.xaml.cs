using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using GTS.Billing.Application;
using GTS.Billing.Infrastructure;
namespace GTS.Billing.Presentation.Wpf;

public partial class MainWindow : Window
{
    private readonly AuthenticationViewModel authentication;
    private readonly CancellationTokenSource lifetime = new();
    private bool closingRequested;
    private bool wasSignedIn;
    private int accessRequest;
    private int settingsRequest;
    private SettingsStore? loginSettings;
    private LoginPresentation loginPresentation=LoginPresentation.Default;

    internal async Task ConfigureLoginAsync(SettingsStore store)
    {
        loginSettings=store;
        try
        {
            await authentication.Lifetime.RunAsync(async ct=>loginPresentation=await store.ReadLoginAsync(ct),lifetime.Token);
            AuthenticationPanel.ApplyLoginPresentation(loginPresentation);
        }
        catch(Exception error) when(error is IOException or InvalidOperationException or System.Data.Common.DbException or OperationCanceledException)
        {
            SafeDiagnostics.Record("login-presentation-unavailable","Warning");
        }
    }
    internal bool IsBillingShellVisible => BillingShell.IsVisible;
    public MainWindow(ShellViewModel viewModel, AuthenticationViewModel authentication, IDatabaseLocation location,
        CompanyViewModel? companies=null,CustomerViewModel? customers=null,CatalogViewModel? catalog=null,
        SecurityAdministrationViewModel? security=null,SecurityAdministrationService? securityService=null,
        SettingsViewModel? settings=null,ApplicationSettingsService? settingsService=null,UnitBrandViewModel? unitBrand=null,
        TaxViewModel? tax=null,NumberingViewModel? numbering=null,SalesInvoiceViewModel? sales=null,
        ProductVariantService? variants=null,EnvelopePrintingViewModel? envelope=null,
        GTS.Billing.Printing.EnvelopePrintService? envelopePrinter=null)
    {
        SettingsDisplay.Instance.Apply(ResolvedSettings.Empty);
        InitializeComponent();
        SettingsDisplay.Instance.Changed+=(_,_)=>ApplyNavigationPosition();
        ApplyNavigationPosition();
        DataContext = viewModel;
        this.authentication = authentication;
        PreviewKeyDown+=(_,eventArgs)=>
        {
            if(!IsBillingShellVisible || !IsEnabled || authentication.Busy || eventArgs.IsRepeat ||
                Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox) return;
            var key=eventArgs.Key==Key.System?eventArgs.SystemKey:eventArgs.Key;
            var gesture=Keyboard.Modifiers switch
            {
                ModifierKeys.None when key is >=Key.F1 and <=Key.F12=>key.ToString(),
                (ModifierKeys.Control|ModifierKeys.Shift) when key is >=Key.A and <=Key.Z=>$"Ctrl+Shift+{key}",
                _=>null
            };
            if(gesture is null) return;
            bool Assigned(string id)=>SettingsDisplay.Current.Text("Configuration.Shortcut."+id,"Disabled")==gesture;
            foreach(var (id,menu) in new[]{("master",MasterMenuHeader),("transactions",TransactionMenuHeader),("printing",PrintingMenuHeader),
                ("administration",AdministrationMenuHeader)})
                if(Assigned(id) && menu.IsVisible && menu.IsEnabled)
                {menu.Focus();menu.IsSubmenuOpen=true;eventArgs.Handled=true;return;}
            var target=ShellViewModel.ModuleCatalog.FirstOrDefault(module=>Assigned(module.Code) && viewModel.CanNavigate(module));
            if(target is null) return;
            viewModel.NavigateCommand.Execute(target);
            eventArgs.Handled=true;
        };
        CompanyPanel.DataContext = companies;
        CustomerPanel.DataContext = customers;
        CatalogPanel.DataContext = catalog;
        CatalogPanel.Variants=variants;
        UnitBrandPanel.DataContext = unitBrand;
        TaxPanel.DataContext=tax;
        NumberingPanel.DataContext=numbering;
        SalesPanel.DataContext=sales;
        if(envelope is not null) EnvelopePanel.Configure(envelope,settingsService,envelopePrinter,
            ()=>viewModel.NavigateCommand.Execute(viewModel.Dashboard));
        if(sales is not null) sales.SessionExpired+=async ()=>await authentication.CheckSessionAsync(lifetime.Token);
        if(numbering is not null) numbering.SessionExpired+=async ()=>await authentication.CheckSessionAsync(lifetime.Token);
        SettingsDisplay.Instance.Changed+=(_,_)=>numbering?.RefreshDisplay();
        if(tax is not null) tax.SessionExpired+=async ()=>await authentication.CheckSessionAsync(lifetime.Token);
        SettingsDisplay.Instance.Changed+=(_,_)=>tax?.RefreshDisplay();
        if (unitBrand is not null) unitBrand.SessionExpired += async () => await authentication.CheckSessionAsync(lifetime.Token);
        SettingsDisplay.Instance.Changed += (_,_) => unitBrand?.RefreshDisplay();
        SecurityPanel.DataContext=security;
        SettingsPanel.DataContext=settings;
        if(settings is not null)
        {
            settings.SessionExpired+=async ()=>await authentication.CheckSessionAsync(lifetime.Token);
            settings.Changed+=async (_,_) =>
            {
                await RefreshSettingsAsync();
                if(loginSettings is not null) await ConfigureLoginAsync(loginSettings);
            };
            settings.PreviewLogin=(key,value,image)=>
            {
                var look=loginPresentation with {Text=new(loginPresentation.Text.Values.SetItem(key,value))};
                if(key=="Login.Logo") look=look with {Logo=image};
                if(key=="Login.Background") look=look with {Background=image};
                var preview=new AuthenticationView();
                preview.ShowDesignPreview(look);
                new Window {Title="Login page preview",Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,
                    Width=Math.Min(850,SystemParameters.WorkArea.Width),Height=Math.Min(720,SystemParameters.WorkArea.Height),Content=preview}.ShowDialog();
            };
            settings.OpenCompany=()=>
            {
                viewModel.SelectedModule=viewModel.Modules.Single(x=>x.Code=="company");
                return Task.CompletedTask;
            };
        }
        if (catalog is not null) catalog.SessionExpired += async () => await authentication.CheckSessionAsync(lifetime.Token);
        if (security is not null) security.SessionExpired+=async ()=>await authentication.CheckSessionAsync(lifetime.Token);
        viewModel.NavigationGuard = target =>
            (securityService is null || viewModel.CanNavigate(target)) &&
            (viewModel.SelectedModule.Code != "company" || target.Code == "company" || CompanyPanel.ConfirmDiscard()) &&
            (viewModel.SelectedModule.Code != "customer" || target.Code == "customer" || CustomerPanel.ConfirmDiscard()) &&
            (viewModel.SelectedModule.Code is not ("category" or "product") || CatalogPanel.ConfirmDiscard()) &&
            (viewModel.SelectedModule.Code!="users" || SecurityPanel.CanLeave()) &&
            (viewModel.SelectedModule.Code!="settings" || settings?.CanLeave()!=false);
        if(customers is not null) customers.SessionExpired += async () => await authentication.CheckSessionAsync(lifetime.Token);
        viewModel.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(ShellViewModel.SelectedModule)) return;
            var isCompany = viewModel.SelectedModule.Code == "company";
            var isCustomer = viewModel.SelectedModule.Code == "customer";
            CompanyPanel.Visibility = isCompany ? Visibility.Visible : Visibility.Collapsed;
            CustomerPanel.Visibility = isCustomer ? Visibility.Visible : Visibility.Collapsed;
            var isCatalog = viewModel.SelectedModule.Code is "category" or "product";
            var isUnitBrand = viewModel.SelectedModule.Code is "unit" or "brand";
            var isTax=TaxModule(viewModel.SelectedModule.Code) is not null;
            var isNumbering=NumberingModule(viewModel.SelectedModule.Code) is not null;
            var isSales=viewModel.SelectedModule.Code=="invoice";
            var isEnvelope=viewModel.SelectedModule.Code=="envelope-printing";
            var isSecurity=viewModel.SelectedModule.Code=="users";
            var isSettings=viewModel.SelectedModule.Code=="settings";
            CatalogPanel.Visibility = isCatalog ? Visibility.Visible : Visibility.Collapsed;
            UnitBrandPanel.Visibility = isUnitBrand ? Visibility.Visible : Visibility.Collapsed;
            TaxPanel.Visibility=isTax?Visibility.Visible:Visibility.Collapsed;
            NumberingPanel.Visibility=isNumbering?Visibility.Visible:Visibility.Collapsed;
            SalesPanel.Visibility=isSales?Visibility.Visible:Visibility.Collapsed;
            EnvelopePanel.Visibility=isEnvelope?Visibility.Visible:Visibility.Collapsed;
            envelope?.Open(isEnvelope);
            SecurityPanel.Visibility=isSecurity?Visibility.Visible:Visibility.Collapsed;
            SettingsPanel.Visibility=isSettings?Visibility.Visible:Visibility.Collapsed;
            PlannedPanel.Visibility = isCompany || isCustomer || isCatalog || isSecurity || isSettings || isUnitBrand || isTax || isNumbering || isSales || isEnvelope ? Visibility.Collapsed : Visibility.Visible;
            if(isSales && authentication.Page==AuthenticationPage.Shell && sales is not null) await sales.OpenAsync();
            else sales?.Clear();
            if(isNumbering && authentication.Page==AuthenticationPage.Shell) await OpenNumberingAsync();
            else numbering?.Clear();
            if(isTax && authentication.Page==AuthenticationPage.Shell) await OpenTaxAsync();
            else tax?.Clear();
            if (!isUnitBrand) unitBrand?.Clear();
            else if (authentication.Page == AuthenticationPage.Shell && unitBrand is not null)
                await unitBrand.OpenAsync(viewModel.SelectedModule.Code == "brand");
            if (!isCatalog) catalog?.Clear();
            else if (authentication.Page == AuthenticationPage.Shell && catalog is not null)
                await catalog.OpenModuleAsync(viewModel.SelectedModule.Code == "product");
            if(isSecurity && authentication.Page==AuthenticationPage.Shell && security is not null) await security.ActivateAsync();
            else if(!isSecurity) security?.Clear();
            if(isSettings && authentication.Page==AuthenticationPage.Shell && settings is not null) await settings.ActivateAsync();
            else if(!isSettings) settings?.Clear();
            if (isCompany && authentication.Page == AuthenticationPage.Shell && companies is not null && !companies.EditorVisible)
                await companies.RefreshAsync();
            if (isCustomer && authentication.Page == AuthenticationPage.Shell && customers is not null && !customers.EditorOpen)
                await customers.RefreshAsync();
        };
        if (companies is not null)
        {
            if(settings is not null && sales is not null) settings.PreviewTemplate=sales.PreviewCompanyTemplateAsync;
            companies.PropertyChanged += async (_,e) =>
            {
                if(e.PropertyName==nameof(CompanyViewModel.CurrentCompany) && settings is not null)
                    settings.CompanyLabel=companies.CurrentCompany;
                if(e.PropertyName != nameof(CompanyViewModel.CurrentCompanyId)) return;
                void ClearCompanyViews() { customers?.Clear(); catalog?.Clear(); unitBrand?.Clear(); tax?.Clear(); numbering?.Clear(); sales?.Clear(); }
                if(Dispatcher.CheckAccess()) ClearCompanyViews();else await Dispatcher.InvokeAsync(ClearCompanyViews);
                if(wasSignedIn && authentication.Page==AuthenticationPage.Shell)
                {
                    await RefreshAccessAsync();
                    await RefreshSettingsAsync();
                    if (viewModel.SelectedModule.Code is "unit" or "brand" && unitBrand is not null)
                        await unitBrand.OpenAsync(viewModel.SelectedModule.Code == "brand");
                    await OpenTaxAsync();
                    await OpenNumberingAsync();
                    if(viewModel.SelectedModule.Code=="invoice" && sales is not null) await sales.OpenAsync();
                }
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
                    await RefreshAccessAsync();
                    await RefreshSettingsAsync();
                    await companies.RefreshCurrentAsync();
                    var before=viewModel.SelectedModule;
                    viewModel.ApplyLandingPage(SettingsDisplay.Current.Get(SettingsRegistry.LandingPage));
                    if(viewModel.SelectedModule!=before) return;
                    if (viewModel.SelectedModule.Code == "company") await companies.RefreshAsync();
                    if (viewModel.SelectedModule.Code == "customer" && customers is not null) await customers.RefreshAsync();
                    if (viewModel.SelectedModule.Code is "category" or "product" && catalog is not null)
                        await catalog.OpenModuleAsync(viewModel.SelectedModule.Code == "product");
                    if (viewModel.SelectedModule.Code=="users" && security is not null) await security.ActivateAsync();
                    if (viewModel.SelectedModule.Code=="settings" && settings is not null) await settings.ActivateAsync();
                }
                else if (authentication.Page != AuthenticationPage.Shell)
                {
                    wasSignedIn = false;
                    accessRequest++;
                    settingsRequest++;
                    ApplyAccess(AccessSnapshot.Denied);
                    SettingsDisplay.Instance.Apply(ResolvedSettings.Empty);
                    viewModel.RefreshDisplay();
                    companies.Clear();
                    customers?.Clear();
                    catalog?.Clear();
                    unitBrand?.Clear();
                    tax?.Clear();
                    numbering?.Clear();
                    sales?.Clear();
                    security?.Clear();
                    settings?.Clear();
                }
                }));
            };
        }
        DiagnosticsStatus.DataContext = authentication;
        authentication.Lifetime.ShutdownStarted += () => IsEnabled = false;
        KeyDown += (_, e) =>
        {
            if(e.Key!=System.Windows.Input.Key.Escape || authentication.Busy || authentication.Lifetime.IsStopping) return;
            if(authentication.Page==AuthenticationPage.Shell && viewModel.SelectedModule!=viewModel.Dashboard)
            {
                e.Handled=true;
                viewModel.NavigateCommand.Execute(viewModel.Dashboard);
            }
            else if(AuthenticationPanel.TryCancelFromKeyboard()) e.Handled=true;
            else if(authentication.Page==AuthenticationPage.Login)
            {
                e.Handled=true;
                if(MessageBox.Show(this,"Exit GTS Billing?","GTS Billing",MessageBoxButton.YesNo,MessageBoxImage.Question,
                    MessageBoxResult.No)==MessageBoxResult.Yes) Close();
            }
        };
        Closing += async (_, e) =>
        {
            if (authentication.Lifetime.IsStopped) return;
            e.Cancel = true;
            if (closingRequested) return;
            if (companies?.HasUnsavedChanges == true && !companies.Busy && companies.PresentationEditor?.Busy!=true && !CompanyPanel.ConfirmDiscard()) return;
            if (customers?.Dirty == true && !customers.Busy && !CustomerPanel.ConfirmDiscard()) return;
            if (catalog?.Dirty == true && !catalog.Busy && !CatalogPanel.ConfirmDiscard()) return;
            if (security?.Dirty==true && !security.Busy && !SecurityPanel.CanLeave()) return;
            if (settings?.HasUnsavedChanges==true && !settings.Busy && !settings.CanLeave()) return;
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
        Activated += async (_, _) =>
        {
            await authentication.CheckSessionAsync(lifetime.Token);
            if(authentication.Page==AuthenticationPage.Shell) await RefreshAccessAsync();
        };
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            authentication.ForgetCodes();
        };
        RefreshShell();

        void ApplyAccess(AccessSnapshot value)
        {
            companies?.SetAccess(value);
            customers?.SetAccess(value);
            catalog?.SetAccess(value);
            unitBrand?.SetAccess(value);
            tax?.SetAccess(value);
            numbering?.SetAccess(value);
            sales?.SetAccess(value);
            envelope?.SetAccess(value);
            security?.SetAccess(value);
            settings?.SetAccess(value);
            viewModel.SetAccess(value);
        }

        static TaxMaster? TaxModule(string code)=>code switch
        {
            "hsnsac"=>TaxMaster.Classifications,"tax"=>TaxMaster.Rates,"taxassignments"=>TaxMaster.Assignments,_=>null
        };
        static NumberingMaster? NumberingModule(string code)=>code switch
        {
            "financialyear"=>NumberingMaster.Years,"documentseries"=>NumberingMaster.Series,_=>null
        };
        async Task OpenNumberingAsync()
        {
            if(numbering is null || NumberingModule(viewModel.SelectedModule.Code) is not NumberingMaster master) return;
            NumberingPanel.ConfigureColumns(master);
            await numbering.OpenAsync(master);
        }
        async Task OpenTaxAsync()
        {
            if(tax is null || TaxModule(viewModel.SelectedModule.Code) is not TaxMaster master) return;
            TaxPanel.ConfigureColumns(master);
            await tax.OpenAsync(master);
        }

        async Task RefreshAccessAsync()
        {
            if(securityService is null || authentication.Lifetime.IsStopping || authentication.Page!=AuthenticationPage.Shell) return;
            var version=++accessRequest;
            try
            {
                var result=await Task.Run(()=>securityService.ExecuteAsync(new(SecurityAction.Access),ct:lifetime.Token),lifetime.Token);
                if(version!=accessRequest || authentication.Page!=AuthenticationPage.Shell) return;
                ApplyAccess(result.Access ?? AccessSnapshot.Denied);
                if(result.Outcome==CompanyOutcome.SessionRequired) await authentication.CheckSessionAsync(lifetime.Token);
            }
            catch(OperationCanceledException) { }
        }

        async Task RefreshSettingsAsync()
        {
            if(settingsService is null || authentication.Lifetime.IsStopping || authentication.Page!=AuthenticationPage.Shell) return;
            var request=++settingsRequest;
            try
            {
                var result=await Task.Run(()=>settingsService.ExecuteAsync(new(SettingsAction.Runtime),lifetime.Token),lifetime.Token);
                if(request!=settingsRequest || authentication.Page!=AuthenticationPage.Shell || result.Runtime is null) return;
                SettingsDisplay.Instance.Apply(result.Runtime);
                companies?.RefreshDisplay();
                catalog?.RefreshDisplay();
                viewModel.RefreshDisplay();
            }
            catch(OperationCanceledException) { }
        }
    }

    private void RefreshShell()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshShell));
            return;
        }
        var signedIn = authentication.Page == AuthenticationPage.Shell && !authentication.Lifetime.IsStopping;
        if(!signedIn || authentication.Busy) AccountMenu.IsSubmenuOpen=false;
        SignInNotice.Text=signedIn && authentication.Message==loginPresentation.Get("Success")?authentication.Message:"";
        BillingShell.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        BillingShell.IsEnabled = !authentication.Busy;
        AuthenticationPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        Regenerate.Visibility = authentication.CanRegenerate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenNavigationSubmenu(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { IsEnabled: true, HasItems: true, IsSubmenuOpen: false } item)
            item.SetCurrentValue(System.Windows.Controls.MenuItem.IsSubmenuOpenProperty, true);
    }

    private void ApplyNavigationPosition()
    {
        var position=SettingsDisplay.Current.Get(SettingsRegistry.NavigationPosition);
        var dock=position switch {"LEFT_NAVIGATION"=>Dock.Left,"RIGHT_NAVIGATION"=>Dock.Right,_=>Dock.Top};
        DockPanel.SetDock(NavigationHost,dock);
        NavigationHost.Width=dock==Dock.Top?double.NaN:180;
        ToggleNavigation.Visibility=dock==Dock.Top?Visibility.Collapsed:Visibility.Visible;
        NavigationMenu.Visibility=Visibility.Visible;
        ToggleNavigation.Content="Hide navigation";
        var panel=new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty,dock==Dock.Top?Orientation.Horizontal:Orientation.Vertical);
        NavigationMenu.ItemsPanel=new ItemsPanelTemplate(panel);
    }

    private void ToggleNavigationClicked(object sender,RoutedEventArgs e)
    {
        var expanded=NavigationMenu.Visibility!=Visibility.Visible;
        NavigationMenu.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
        ToggleNavigation.Content=expanded?"Hide navigation":"☰";
        NavigationHost.Width=expanded?180:58;
    }

    private async void RegenerateClicked(object sender, RoutedEventArgs e)
    {
        if (!CompanyPanel.ConfirmDiscard()) return;
        if (!CustomerPanel.ConfirmDiscard()) return;
        if (!CatalogPanel.ConfirmDiscard()) return;
        if (!SecurityPanel.CanLeave()) return;
        if (SettingsPanel.DataContext is SettingsViewModel { IsOpen:true } settings && !settings.CanLeave()) return;
        await authentication.BeginRegenerationAsync(lifetime.Token);
    }
    private void ChangePasswordClicked(object sender,RoutedEventArgs e)
    {
        if(CompanyPanel.ConfirmDiscard() && CustomerPanel.ConfirmDiscard() && CatalogPanel.ConfirmDiscard() && SecurityPanel.CanLeave() &&
            (SettingsPanel.DataContext is not SettingsViewModel { IsOpen:true } settings || settings.CanLeave()))
            authentication.BeginPasswordChange();
    }
    private async void LogoutClicked(object sender, RoutedEventArgs e)
    {
        if (CompanyPanel.DataContext is CompanyViewModel { HasUnsavedChanges: true, Busy: false } company &&
            company.PresentationEditor?.Busy!=true && !CompanyPanel.ConfirmDiscard()) return;
        if (CustomerPanel.DataContext is CustomerViewModel { Dirty: true, Busy: false } && !CustomerPanel.ConfirmDiscard()) return;
        if (CatalogPanel.DataContext is CatalogViewModel { Dirty: true, Busy: false } && !CatalogPanel.ConfirmDiscard()) return;
        if (SecurityPanel.DataContext is SecurityAdministrationViewModel { Dirty:true, Busy:false } && !SecurityPanel.CanLeave()) return;
        if (SettingsPanel.DataContext is SettingsViewModel { HasUnsavedChanges:true, Busy:false } settings && !settings.CanLeave()) return;
        try { await authentication.LogoutAsync(); }
        catch
        {
            MessageBox.Show("GTS Billing could not finish logout safely. Exclusive ownership is retained.",
                "GTS Billing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
