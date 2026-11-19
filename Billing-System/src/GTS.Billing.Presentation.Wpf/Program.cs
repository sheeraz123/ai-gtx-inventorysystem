using System.Windows;

namespace GTS.Billing.Presentation.Wpf;

internal static class Program
{
    internal const string BusyMessage = "GTS Billing is already running on this computer. Close the other instance and try again.";
    internal const string FailureMessage = "GTS Billing could not verify exclusive access. The database was not opened.";

    [STAThread]
    public static int Main(string[] args)
    {
        DeploymentIdentity identity;
        try { identity = DeploymentIdentity.Load(DeploymentIdentity.MachinePath); }
        catch
        {
            Report(OwnershipResult.InvalidConfiguration, args);
            return 3;
        }
        return Run(identity, args);
    }

    // Test composition is internal. The shipped CLI has no identity/path/namespace override.
    internal static int Run(DeploymentIdentity identity, string[] args, bool? testAllowExplicitOverrides = null, string? testSmokeEvidenceDirectory = null)
    {
        var result = GlobalInstanceLock.TryAcquire(identity, out var ownership);
        if (ownership is null)
        {
            Report(result, args);
            return result == OwnershipResult.Busy ? 2 : 3;
        }
        SafeDiagnostics.Record(result == OwnershipResult.Abandoned ? "ownership-abandoned" : "ownership-acquired", "Information");
        try
        {
            var app = new App(args)
            {
                TestAllowExplicitOverrides = testAllowExplicitOverrides,
                TestSmokeEvidenceDirectory = testSmokeEvidenceDirectory
            };
            app.InitializeComponent();
            var exit = app.Run();
            if (!app.ShutdownFinished)
                Environment.FailFast("Application exited before operation draining completed.");
            ownership.Dispose();
            return exit;
        }
        catch
        {
            // Never unwind the mutex owner while an untracked surviving worker could still use SQLite.
            Environment.FailFast("Unrecoverable Billing process failure.");
            return 1;
        }
    }

    private static void Report(OwnershipResult result, string[] args)
    {
        SafeDiagnostics.Record("ownership-" + result.ToString().ToLowerInvariant(), "Warning");
        // Automation exits with a typed code. Interactive launches receive the required diagnostic.
        if (!args.Contains("--smoke-test") && !args.Contains("--initialize-development-database") &&
            !args.Contains("--export-schema"))
            MessageBox.Show((result == OwnershipResult.Busy ? BusyMessage : FailureMessage) +
                (SafeDiagnostics.LastOutcome == DiagnosticWriteResult.Written ? "" : "\nDiagnostics are unavailable."),
                "GTS Billing",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
