using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using GTS.Billing.Application;
using GTS.Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GTS.Billing.Presentation.Wpf;

public partial class App : System.Windows.Application
{
    private IHost? host;
    private readonly string[]? startupArguments;
    internal bool? TestAllowExplicitOverrides { get; init; }
    internal string? TestSmokeEvidenceDirectory { get; init; }
    private readonly ApplicationLifetime lifetime = new();
    internal bool ShutdownFinished => lifetime.IsStopped;
    private int exitCode;

    public App(string[]? arguments = null)
    {
        startupArguments = arguments;
        lifetime.HostCleanup = async () =>
        {
            if (host is not null)
            {
                await host.StopAsync(CancellationToken.None);
                host.Dispose();
                host = null;
            }
        };
        lifetime.ShutdownCompleted += () => Shutdown(exitCode);
        SessionEnding += (_, ending) =>
        {
            ending.Cancel = true;
            _ = RequestShutdownAsync(0);
        };
    }

    private async Task RequestShutdownAsync(int code)
    {
        exitCode = code;
        try { await lifetime.ShutdownAsync(); }
        catch
        {
            MessageBox.Show("GTS Billing could not finish shutdown safely. Exclusive ownership is retained. Check active operations before closing the process.",
                "GTS Billing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EventManager.RegisterClassHandler(typeof(Window),System.Windows.Input.Keyboard.KeyDownEvent,
            new System.Windows.Input.KeyEventHandler((sender,args)=>
            {
                if(args.Key!=System.Windows.Input.Key.Escape || sender is not Window {Owner:not null} owned) return;
                args.Handled=true;
                owned.Close();
            }));
        var args = startupArguments ?? e.Args;
        var startupStage = "configuration";
        DispatcherUnhandledException += OnDispatcherFailure;
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Environment.FailFast("Unrecoverable Billing runtime failure.");
        try
        {
            if (args.Length == 2 && args[0] == "--export-schema")
            {
                await lifetime.RunAsync(async token =>
                {
                    using var db = DevelopmentDatabase.CreateContext(":memory:", true);
                    await File.WriteAllTextAsync(args[1], db.Database.GenerateCreateScript(), token);
                });
                await RequestShutdownAsync(0);
                return;
            }
            var initialize = args.Contains("--initialize-development-database");
            var smoke = args.Contains("--smoke-test");
            var recovery = args is ["--complete-unit-brand-recovery", _] ? args[1] : null;
            string? path = null;
            for (var i = 0; recovery is null && i < args.Length; i++)
            {
                if (args[i] == "--database" && i + 1 < args.Length)
                    path = args[++i];
                else if (args[i] is not ("--initialize-development-database" or "--smoke-test"))
                    throw new InvalidOperationException("Unknown startup arguments.");
            }
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
            var optInText = builder.Configuration["DevelopmentDatabase:AllowExplicitOverrides"];
            var allowOverrides = optInText is null ? false :
                bool.TryParse(optInText, out var parsedOptIn) ? parsedOptIn :
                throw new InvalidOperationException("Invalid protected development policy.");
            var selectedPath = DatabasePathPolicy.Select(builder.Configuration["DevelopmentDatabase:Path"], path,
                TestAllowExplicitOverrides ?? allowOverrides, initialize);
            builder.Logging.ClearProviders();
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            builder.Logging.AddProvider(new SafeDiagnostics());
            builder.Services.AddSingleton<IDatabaseLocation>(new ConfiguredDatabaseLocation(selectedPath));
            builder.Services.AddSingleton(lifetime);
            builder.Services.AddSingleton<IApplicationOperationCoordinator>(sp => sp.GetRequiredService<ApplicationLifetime>());
            builder.Services.AddSingleton<DevelopmentDatabase>();
            builder.Services.AddSingleton<DevelopmentDatabaseMaintenance>();
            builder.Services.AddSingleton<IDevelopmentDatabase>(sp => sp.GetRequiredService<DevelopmentDatabase>());
            builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
            builder.Services.AddSingleton<IAuthenticationStore, AuthenticationStore>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<AuthenticationService>();
            builder.Services.AddSingleton<IApplicationSession>(sp => sp.GetRequiredService<AuthenticationService>());
            builder.Services.AddSingleton<ICompanyStore, CompanyStore>();
            builder.Services.AddSingleton<CompanyService>();
            builder.Services.AddSingleton<ICompanyPresentationStore, CompanyPresentationStore>();
            builder.Services.AddSingleton<ICompanyImageProcessor, CompanyImageProcessor>();
            builder.Services.AddSingleton<CompanyPresentationService>();
            builder.Services.AddSingleton<CompanyViewModel>();
            builder.Services.AddSingleton<ICustomerStore, CustomerStore>();
            builder.Services.AddSingleton<ICustomerFinancialSource, UnconfiguredCustomerFinancialSource>();
            builder.Services.AddSingleton<CustomerService>();
            builder.Services.AddSingleton<CustomerViewModel>();
            builder.Services.AddSingleton<IProductCatalogStore, ProductCatalogStore>();
            builder.Services.AddSingleton<ProductCatalogService>();
            builder.Services.AddSingleton<IProductVariantStore,ProductVariantStore>();
            builder.Services.AddSingleton<ProductVariantService>();
            builder.Services.AddSingleton<CatalogViewModel>();
            builder.Services.AddSingleton<IUnitBrandStore, UnitBrandStore>();
            builder.Services.AddSingleton<UnitBrandService>();
            builder.Services.AddSingleton<UnitBrandViewModel>();
            builder.Services.AddSingleton<ITaxCatalogStore, TaxCatalogStore>();
            builder.Services.AddSingleton<TaxCatalogService>();
            builder.Services.AddSingleton<TaxViewModel>();
            builder.Services.AddSingleton<INumberingStore, NumberingStore>();
            builder.Services.AddSingleton<NumberingService>();
            builder.Services.AddSingleton<NumberingViewModel>();
            builder.Services.AddSingleton<ISalesInvoiceStore,SalesInvoiceStore>();
            builder.Services.AddSingleton<SalesInvoiceService>();
            builder.Services.AddSingleton<IDocumentNumberFinalizer>(sp=>sp.GetRequiredService<SalesInvoiceService>());
            builder.Services.AddSingleton<SalesInvoiceViewModel>();
            builder.Services.AddSingleton<ISecurityAdministrationStore, SecurityAdministrationStore>();
            builder.Services.AddSingleton<SecurityAdministrationService>();
            builder.Services.AddSingleton<SecurityAdministrationViewModel>();
            builder.Services.AddSingleton<SettingsStore>();
            builder.Services.AddSingleton<ISettingsStore>(sp=>sp.GetRequiredService<SettingsStore>());
            builder.Services.AddSingleton<ApplicationSettingsService>();
            builder.Services.AddSingleton<SettingsViewModel>();
            builder.Services.AddSingleton<AuthenticationViewModel>();
            var configured = !string.IsNullOrWhiteSpace(selectedPath);
            const string status = "Existing plaintext DEVELOPMENT database verified. Synthetic data only.";
            builder.Services.AddSingleton(new ShellViewModel(status));
            builder.Services.AddSingleton<MainWindow>();
            host = builder.Build();
            await lifetime.RunAsync(token => host.StartAsync(token));
            if (recovery is not null)
            {
                startupStage = "database update";
                var maintenance = host.Services.GetRequiredService<DevelopmentDatabaseMaintenance>();
                await lifetime.RunAsync(token => Task.Run(() => maintenance.CompleteUnitBrandRecoveryAsync(recovery, token), token));
                SafeDiagnostics.Record("database-upgrade-completed", "Information");
            }
            if (initialize || configured)
            {
                startupStage = "database verification";
                var database = host.Services.GetRequiredService<IDevelopmentDatabase>();
                try
                {
                await lifetime.RunAsync(token => Task.Run(async () =>
                {
                    if (initialize) await database.InitializeAsync(token);
                    await database.VerifyExistingAsync(token);
                }, token));
                }
                catch (DatabaseStartupException problem) when (problem.Problem == DatabaseStartupProblem.UpgradeRequired && !initialize && !smoke)
                {
                    var maintenance = host.Services.GetRequiredService<DevelopmentDatabaseMaintenance>();
                    IReadOnlyList<string> pending = [];
                    await lifetime.RunAsync(token => Task.Run(async () => pending = await maintenance.GetReviewedPendingAsync(token), token));
                    if (pending.Count > 0)
                    {
                        SafeDiagnostics.Record("database-upgrade-required", "Information");
                        var approved = MessageBox.Show("A reviewed development database update is required:\n\n" +
                            string.Join("\n", pending) + "\n\nGTS will first create and verify a safety backup, then check that your existing accounts and records are preserved.\n\nKeep the application open until verification finishes. Update now?",
                            "GTS Billing — database update required", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
                        if (lifetime.IsStopping) return;
                        if (!approved) { await RequestShutdownAsync(0); return; }
                        startupStage = "database update";
                        SafeDiagnostics.Record("database-upgrade-started", "Information");
                        await lifetime.RunAsync(token => Task.Run(() => maintenance.ApplyReviewedAsync(pending, token), token));
                        SafeDiagnostics.Record("database-upgrade-completed", "Information");
                    }
                    await lifetime.RunAsync(token => Task.Run(() => database.VerifyExistingAsync(token), token));
                }
                if (initialize)
                {
                    SafeDiagnostics.Record("development-initialized", "Information");
                    await RequestShutdownAsync(0);
                    return;
                }
            }
            startupStage = "authentication startup";
            var auth = host.Services.GetRequiredService<AuthenticationViewModel>();
            SettingsDisplay.Instance.Apply(ResolvedSettings.Empty);
            await auth.StartAsync(configured);
            var window = host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            if(configured) await window.ConfigureLoginAsync(host.Services.GetRequiredService<SettingsStore>());
            if (smoke)
                window.ContentRendered += (_, _) => Dispatcher.BeginInvoke(new Action(() => RunSmoke(window, auth)), DispatcherPriority.ApplicationIdle);
            window.Show();
            SafeDiagnostics.Record("shell-started", "Information");
        }
        catch (Exception error)
        {
            var notice = StartupFailurePresentation.Describe(error, startupStage);
            SafeDiagnostics.Record(notice.Code, "Error");
            if (!lifetime.IsStopping && !args.Contains("--smoke-test") && !args.Contains("--initialize-development-database") && !args.Contains("--export-schema"))
                MessageBox.Show(notice.Message +
                    (SafeDiagnostics.LastOutcome == DiagnosticWriteResult.Written ? "" : "\nDiagnostics are unavailable."),
                    "GTS Billing — startup stopped", MessageBoxButton.OK, MessageBoxImage.Error);
            await RequestShutdownAsync(1);
        }
    }
    private async void RunSmoke(MainWindow window, AuthenticationViewModel auth)
    {
        try
        {
            var vm = (ShellViewModel)window.DataContext;
            foreach (var module in vm.Modules)
                vm.NavigateCommand.Execute(module);
            if (!window.IsVisible || window.IsBillingShellVisible || auth.Page == AuthenticationPage.Shell ||
                vm.SelectedModule != vm.Modules[^1] || vm.Modules.Any(x => x.Status != (x.Code == "company" ? "Available" : "Planned")))
                throw new InvalidOperationException();
            var evidenceDirectory = TestSmokeEvidenceDirectory ?? SafeDiagnostics.OperationalDirectory;
            // Smoke evidence is mandatory. Missing/unwritable storage explicitly fails verification.
            File.WriteAllText(Path.Combine(evidenceDirectory, "smoke.json"),
                JsonSerializer.Serialize(new
                {
                    windowLoaded = window.IsLoaded,
                    windowVisible = window.IsVisible,
                    modulesNavigated = vm.Modules.Count,
                    allOtherModulesPlanned = true,
                    companyMasterAvailable = true,
                    shellLocked = true,
                    authenticationPage = auth.Page.ToString(),
                    timestampUtc = DateTime.UtcNow
                }));
            await RequestShutdownAsync(0);
        }
        catch { SafeDiagnostics.Record("smoke-failed", "Error"); await RequestShutdownAsync(1); }
    }
    private void OnDispatcherFailure(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Environment.FailFast("Unrecoverable Billing dispatcher failure.");
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (!lifetime.IsStopped) Environment.FailFast("WPF exited before tracked work drained.");
        base.OnExit(e);
    }
}
