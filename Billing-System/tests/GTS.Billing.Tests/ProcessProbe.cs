using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using GTS.Billing.Infrastructure;
using GTS.Billing.Domain;
using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Tests;

// Test executable entry point only. The shipped WPF CLI cannot select a synthetic identity.
internal static class ProcessProbe
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            using var diagnostics = new DiagnosticsFixture();
            if (args.Length < 2) return 90;
            var identity = DeploymentIdentity.Load(args[1]);
            if (args[0] == "app") return Program.Run(identity, args.Skip(2).ToArray(), true, diagnostics.DirectoryPath);
            if (args[0] == "installed-app") return Program.Run(identity, args.Skip(2).ToArray(), false, diagnostics.DirectoryPath);
            if (args[0] == "missing-evidence-app") return Program.Run(identity, args.Skip(2).ToArray(), true, Path.Combine(diagnostics.DirectoryPath, "absent"));
            if (args[0] == "environment-app")
            {
                Environment.SetEnvironmentVariable("DevelopmentDatabase__AllowExplicitOverrides", "true", EnvironmentVariableTarget.Process);
                return Program.Run(identity, args.Skip(2).ToArray(), null, diagnostics.DirectoryPath);
            }
            var result = GlobalInstanceLock.TryAcquire(identity, out var ownership);
            Console.WriteLine(result);
            Console.Out.Flush();
            if (ownership is null) return result == OwnershipResult.Busy ? 2 : 3;
            if (args[0] == "recover-app")
            {
                var app = new App(["--smoke-test", "--database", args[2]])
                {
                    TestAllowExplicitOverrides = true, TestSmokeEvidenceDirectory = diagnostics.DirectoryPath
                };
                app.InitializeComponent();
                var exit = app.Run();
                if (!app.ShutdownFinished) return 93;
                ownership.Dispose();
                return exit;
            }
            if (args[0] == "logout-app")
            {
                var app = new App(["--database", args[2]]) { TestAllowExplicitOverrides = true };
                app.InitializeComponent();
                var started = false;
                app.Activated += async (_, _) =>
                {
                    if (started || app.MainWindow is not MainWindow window) return;
                    started = true;
                    try
                    {
                        var view = (AuthenticationView)window.FindName("AuthenticationPanel");
                        var vm = (AuthenticationViewModel)view.DataContext;
                        var password = Guid.NewGuid().ToString("N") + " Synthetic";
                        await vm.SubmitAsync("Test.Logout", password, password, "");
                        await vm.ContinueAsync();
                        await vm.SubmitAsync("Test.Logout", password, "", "");
                        if (vm.Page != AuthenticationPage.Shell) Environment.FailFast("Synthetic login failed.");
                        Console.WriteLine("Authenticated");
                        Console.Out.Flush();
                        await Task.Run(() => Console.ReadLine());
                        await vm.LogoutAsync();
                    }
                    catch { Environment.FailFast("Synthetic logout smoke failed."); }
                };
                var exit = app.Run();
                if (!app.ShutdownFinished) return 93;
                ownership.Dispose();
                return exit;
            }
            if (args[0] == "drain")
            {
                var lifetime = new ApplicationLifetime();
                using var finish = new ManualResetEventSlim();
                using var ready = new ManualResetEventSlim();
                var work = lifetime.RunAsync(_ => Task.Run(() =>
                {
                    using var db = DevelopmentDatabase.CreateContext(args[2]);
                    using var transaction = db.Database.BeginTransaction();
                    db.Add(new Company { Code = "DRAIN_TEST", LegalName = "Synthetic" });
                    db.SaveChanges();
                    ready.Set();
                    finish.Wait(TimeSpan.FromSeconds(30));
                    transaction.Rollback();
                }));
                if (!ready.Wait(TimeSpan.FromSeconds(15))) return 92;
                Console.WriteLine("WorkReady");
                Console.Out.Flush();
                Console.ReadLine();
                var shutdown = lifetime.ShutdownAsync();
                Console.WriteLine("Draining");
                Console.Out.Flush();
                Console.ReadLine();
                finish.Set();
                Task.WhenAll(work, shutdown).GetAwaiter().GetResult();
                ownership.Dispose();
                return 0;
            }
            if (args[0] == "transaction-crash")
            {
                using var db = DevelopmentDatabase.CreateContext(args[2]);
                db.Database.OpenConnection();
                using var transaction = db.Database.BeginTransaction();
                db.Add(new Company { Code = "CRASH_TEST", LegalName = "Synthetic uncommitted record" });
                db.SaveChanges();
                Console.WriteLine("TransactionReady");
                Console.Out.Flush();
                Console.ReadLine();
                Environment.Exit(9);
            }
            // Deliberate test termination skips managed cleanup and leaves OS crash recovery to the next owner.
            Console.ReadLine();
            if (args[0] is "crash" or "transaction-crash") Environment.Exit(9);
            ownership.Dispose();
            return 0;
        }
        catch { return 91; }
    }
}
