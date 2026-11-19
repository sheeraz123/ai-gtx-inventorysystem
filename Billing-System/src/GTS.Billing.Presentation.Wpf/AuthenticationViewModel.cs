using CommunityToolkit.Mvvm.ComponentModel;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public enum AuthenticationPage { Unavailable, Setup, Login, Recover, Regenerate, Codes, Shell, Closing, ChangePassword }

public sealed class AuthenticationViewModel : ObservableObject
{
    private readonly AuthenticationService service;
    private readonly SynchronizationContext? uiContext = SynchronizationContext.Current;
    private readonly DiagnosticsBackend diagnostics = SafeDiagnostics.Backend;
    public ApplicationLifetime Lifetime { get; }
    internal LoginPresentation LoginPresentation {get;set;}=LoginPresentation.Default;
    public string ClosingMessage => Lifetime.IsWaitingForOperations
        ? "Waiting for an active operation to finish. GTS Billing will close automatically."
        : "Closing GTS Billing, please wait…";
    public string DiagnosticsWarning => diagnostics.LastOutcome == DiagnosticWriteResult.Written
        ? "" : "Diagnostics unavailable. Events may not have been saved. Check the approved log directory.";

    public AuthenticationViewModel(AuthenticationService service, ApplicationLifetime? lifetime = null)
    {
        this.service = service;
        Lifetime = lifetime ?? new ApplicationLifetime();
        Lifetime.DrainStateChanged += () => NotifyOnUi(nameof(ClosingMessage));
        diagnostics.StatusChanged += () => NotifyOnUi(nameof(DiagnosticsWarning));
        Lifetime.SessionCleanup = async () =>
        {
            try { await Task.Run(() => service.LogoutAsync()); }
            catch { SafeDiagnostics.Record("logout-audit-failed", "Warning"); }
            ForgetCodes();
            CanRegenerate = false;
            Page = AuthenticationPage.Closing;
        };
        Lifetime.ShutdownStarted += () =>
        {
            ForgetCodes();
            CanRegenerate = false;
            Page = AuthenticationPage.Closing;
        };
    }
    private void NotifyOnUi(string property)
    {
        if (uiContext is not null && SynchronizationContext.Current != uiContext)
            uiContext.Post(_ => OnPropertyChanged(property), null);
        else OnPropertyChanged(property);
    }
    private AuthenticationPage page;
    private string message = "";
    private bool busy;
    private IReadOnlyList<string> codes = Array.Empty<string>();
    private AuthenticationPage returnFromCodes;
    public AuthenticationPage Page { get => page; private set => SetProperty(ref page, value); }
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public bool Busy { get => busy; private set => SetProperty(ref busy, value); }
    public IReadOnlyList<string> Codes => codes;
    public bool CanRegenerate { get; private set; }
    public bool PasswordChangeRequired { get; private set; }

    public async Task StartAsync(bool configured, CancellationToken ct = default)
    {
        if (!configured) { Page = AuthenticationPage.Unavailable; return; }
        await Lifetime.RunAsync(async token =>
        {
            var entry = await Task.Run(() => service.EntryAsync(token), token);
            Page = entry == AuthenticationEntry.Setup ? AuthenticationPage.Setup : AuthenticationPage.Login;
        }, ct);
    }

    public async Task BeginRecoveryAsync(CancellationToken ct = default)
    {
        if (!Busy && Page == AuthenticationPage.Login)
        {
            Message = "";
            Page = AuthenticationPage.Recover;
        }
        await Task.CompletedTask;
    }

    public async Task BeginRegenerationAsync(CancellationToken ct = default)
    {
        if (Busy || Page != AuthenticationPage.Shell) return;
        await Run(async workToken =>
        {
            var identity = await Task.Run(() => service.GetCurrentAsync(workToken), workToken);
            if (identity is null) Page = AuthenticationPage.Login;
            else if (identity.IsSuperAdmin) Page = AuthenticationPage.Regenerate;
        }, ct);
    }

    public async Task SubmitAsync(string username, string password, string confirmation, string code, CancellationToken ct = default)
    {
        var submittedPage = Page;
        if (Busy || submittedPage is not (AuthenticationPage.Setup or AuthenticationPage.Login or AuthenticationPage.Recover or AuthenticationPage.Regenerate or AuthenticationPage.ChangePassword)) return;
        await Run(async workToken =>
        {
            var result = await Task.Run(() => submittedPage switch
            {
                AuthenticationPage.Setup => service.SetupAsync(username, password, confirmation, workToken),
                AuthenticationPage.Login => service.LoginAsync(username, password, workToken),
                AuthenticationPage.Recover => service.RecoverAsync(code, password, confirmation, workToken),
                AuthenticationPage.ChangePassword => service.ChangePasswordAsync(code,password,confirmation,workToken),
                _ => service.RegenerateAsync(password, workToken)
            }, workToken);
            if (result.Outcome == AuthenticationOutcome.Success)
            {
                if (result.Codes is not null)
                {
                    codes = result.Codes;
                    returnFromCodes = submittedPage == AuthenticationPage.Setup ? AuthenticationPage.Login : AuthenticationPage.Shell;
                    OnPropertyChanged(nameof(Codes));
                    Page = AuthenticationPage.Codes;
                }
                else if (submittedPage == AuthenticationPage.Login)
                {
                    var identity = await Task.Run(() => service.GetCurrentAsync(workToken), workToken);
                    CanRegenerate = identity?.IsSuperAdmin == true;
                    PasswordChangeRequired=identity?.MustChangePassword==true;
                    Page = identity is null ? AuthenticationPage.Login : PasswordChangeRequired ? AuthenticationPage.ChangePassword : AuthenticationPage.Shell;
                    if(Page==AuthenticationPage.Shell) Message=LoginPresentation.Get("Success");
                }
                else
                {
                    Page = AuthenticationPage.Login;
                    PasswordChangeRequired=false;
                    CanRegenerate=false;
                    Message = submittedPage==AuthenticationPage.ChangePassword ? "Password changed. Sign in again." :
                        "Password reset. Sign in with the new password.";
                }
                return;
            }
            if (result.Outcome is AuthenticationOutcome.AlreadyConfigured or AuthenticationOutcome.SessionRequired)
                Page = AuthenticationPage.Login;
            Message = result.Outcome switch
            {
                AuthenticationOutcome.InvalidInput or AuthenticationOutcome.Rejected when submittedPage==AuthenticationPage.Login =>
                    LoginPresentation.Get("Invalid"),
                AuthenticationOutcome.InvalidInput when submittedPage==AuthenticationPage.ChangePassword =>
                    "Enter matching new passwords of at least 15 characters (maximum 512 UTF-16 units). Use a different passphrase and avoid username-based passwords.",
                AuthenticationOutcome.InvalidInput => "Check the username format and enter matching passwords of at least 15 characters (maximum 512 UTF-16 units).",
                AuthenticationOutcome.Throttled => "Too many attempts. Wait up to 30 seconds, then try again.",
                AuthenticationOutcome.AlreadyConfigured => "Setup is complete. Sign in to continue.",
                AuthenticationOutcome.SessionRequired => "Sign in again to continue.",
                _ when submittedPage == AuthenticationPage.Setup => "Setup could not use that username. Choose a different one.",
                _ when submittedPage == AuthenticationPage.Recover => "Recovery was not accepted. Check the code and try again.",
                _ => "Sign-in or password verification failed."
            };
        }, ct);
    }

    public void BeginPasswordChange()
    {
        if (Busy || Page!=AuthenticationPage.Shell) return;
        Message="";
        PasswordChangeRequired=false;
        Page=AuthenticationPage.ChangePassword;
    }

    public async Task ContinueAsync(CancellationToken ct = default)
    {
        if (Busy || Page != AuthenticationPage.Codes) return;
        ForgetCodes();
        Page = AuthenticationPage.Login;
        if (returnFromCodes == AuthenticationPage.Shell)
            await Run(async workToken =>
            {
                if (await Task.Run(() => service.GetCurrentAsync(workToken), workToken) is not null) Page = AuthenticationPage.Shell;
            }, ct);
    }

    public async Task CancelAsync(CancellationToken ct = default)
    {
        if (Busy) return;
        Message = "";
        if (Page == AuthenticationPage.Recover) Page = AuthenticationPage.Login;
        else if (Page == AuthenticationPage.Regenerate)
        {
            Page = AuthenticationPage.Login;
            await Run(async workToken =>
            {
                if (await Task.Run(() => service.GetCurrentAsync(workToken), workToken) is not null) Page = AuthenticationPage.Shell;
            }, ct);
        }
        else if (Page==AuthenticationPage.ChangePassword && !PasswordChangeRequired)
        {
            Page=AuthenticationPage.Login;
            await Run(async workToken =>
            {
                if (await Task.Run(()=>service.GetCurrentAsync(workToken),workToken) is { MustChangePassword:false })
                    Page=AuthenticationPage.Shell;
            },ct);
        }
    }

    public async Task LogoutAsync()
    {
        if (Busy || Lifetime.IsStopping || Page != AuthenticationPage.Shell) return;
        await Run(async ct =>
        {
            await Task.Run(() => service.LogoutAsync(ct),ct);
            ForgetCodes();
            CanRegenerate=false;
            PasswordChangeRequired=false;
            if(!Lifetime.IsStopping) Page=AuthenticationPage.Login;
        },CancellationToken.None,closeShellOnFailure:true);
    }

    public async Task CheckSessionAsync(CancellationToken ct = default)
    {
        if (Busy || Page != AuthenticationPage.Shell) return;
        await Run(async workToken =>
        {
            var identity=await Task.Run(()=>service.GetCurrentAsync(workToken),workToken);
            if (identity is null) Page=AuthenticationPage.Login;
            else if (identity.MustChangePassword)
            {
                PasswordChangeRequired=true;
                Page=AuthenticationPage.ChangePassword;
            }
        }, ct, closeShellOnFailure: true);
    }

    public void ForgetCodes()
    {
        codes = Array.Empty<string>();
        OnPropertyChanged(nameof(Codes));
    }

    private async Task Run(Func<CancellationToken, Task> operation, CancellationToken ct, bool closeShellOnFailure = false)
    {
        Busy = true;
        Message = "";
        try { await Lifetime.RunAsync(operation, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || Lifetime.IsStopping) { }
        catch
        {
            if (closeShellOnFailure && !Lifetime.IsStopping)
            {
                ForgetCodes();
                CanRegenerate=false;
                PasswordChangeRequired=false;
                Page=AuthenticationPage.Login;
            }
            SafeDiagnostics.Record("authentication-operation-failed", "Error");
            Message = Page==AuthenticationPage.Login?LoginPresentation.Get("Failure"):
                "The operation could not be confirmed. Reopen the existing development database and check its availability before retrying.";
        }
        finally { Busy = false; }
    }
}
