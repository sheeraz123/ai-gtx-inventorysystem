using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using GTS.Billing.Application;
using GTS.Billing.Printing;
using Microsoft.Win32;

namespace GTS.Billing.Presentation.Wpf;

public partial class AuthenticationView : UserControl
{
    private AuthenticationViewModel? model;
    private IDatabaseLocation? location;
    private CancellationToken token;
    private AuthenticationPage? lastPage;

    public AuthenticationView() => InitializeComponent();

    internal void Attach(AuthenticationViewModel vm, IDatabaseLocation databaseLocation, CancellationToken ct)
    {
        model = vm; location = databaseLocation; token = ct;
        DataContext = vm;
        vm.PropertyChanged += Changed;
        Print.Command = new RelayCommand(() => DocumentAction(true));
        Save.Command = new RelayCommand(() => DocumentAction(false));
        Refresh();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Refresh()
    {
        if (model is null) return;
        var page = model.Page;
        if (lastPage != page)
        {
            ClearInputs();
            Username.Clear();
            DocumentStatus.Text = "";
            lastPage = page;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (model.Page != page || model.Busy) return;
                if (page is AuthenticationPage.Setup or AuthenticationPage.Login) Username.Focus();
                else if (page == AuthenticationPage.Recover) Code.Focus();
                else if (page == AuthenticationPage.Regenerate) PasswordField.Focus();
                else if (page == AuthenticationPage.Codes) Continue.Focus();
            }), DispatcherPriority.Input);
        }
        Heading.Text = page switch
        {
            AuthenticationPage.Setup => "Set up your Super Admin",
            AuthenticationPage.Login => "Sign in to GTS Billing",
            AuthenticationPage.Recover => "Recover the Super Admin account",
            AuthenticationPage.Regenerate => "Generate new recovery codes",
            AuthenticationPage.Codes => "Save your ten recovery codes now",
            AuthenticationPage.Closing => "Closing GTS Billing, please wait…",
            _ => "No development database configured"
        };
        Instructions.Text = page switch
        {
            AuthenticationPage.Setup => "Use a username of 3–40 ASCII letters, digits, dots, underscores or hyphens. Choose a passphrase of at least 15 characters.",
            AuthenticationPage.Login => "Use your separate GTS application credentials.",
            AuthenticationPage.Recover => "Enter an unused Super Admin code and a new passphrase. No built-in bypass exists if you lose the password and all codes. Codes do not decrypt a database.",
            AuthenticationPage.Regenerate => "Enter your current password. All previous unused codes will be revoked when the new set commits.",
            AuthenticationPage.Codes => "Sensitive secrets. Print or save them to a private location. These codes cannot be shown again. Continue discards this display.",
            AuthenticationPage.Closing => model.ClosingMessage,
            _ => "Explicitly initialize or configure a synthetic development database. The billing shell is locked."
        };
        Form.Visibility = Visible(page is AuthenticationPage.Setup or AuthenticationPage.Login or AuthenticationPage.Recover or AuthenticationPage.Regenerate);
        UsernameRow.Visibility = Visible(page is AuthenticationPage.Setup or AuthenticationPage.Login);
        CodeRow.Visibility = Visible(page == AuthenticationPage.Recover);
        ConfirmationRow.Visibility = Visible(page is AuthenticationPage.Setup or AuthenticationPage.Recover);
        Recovery.Visibility = Visible(page == AuthenticationPage.Login);
        Cancel.Visibility = Visible(page is AuthenticationPage.Recover or AuthenticationPage.Regenerate);
        CodePanel.Visibility = Visible(page == AuthenticationPage.Codes);
        CodeList.ItemsSource = page == AuthenticationPage.Codes ? model.Codes : null;
        Form.IsEnabled = CodePanel.IsEnabled = !model.Busy && !model.Lifetime.IsStopping;
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private async void SubmitClicked(object sender, RoutedEventArgs e)
    {
        if (model is null || model.Busy) return;
        var task = model.SubmitAsync(Username.Text, PasswordField.Password, Confirmation.Password, Code.Password, token);
        ClearInputs();
        await task;
    }

    private void ClearInputs() { PasswordField.Clear(); Confirmation.Clear(); Code.Clear(); }
    private async void RecoveryClicked(object sender, RoutedEventArgs e) { if (model is not null) await model.BeginRecoveryAsync(token); }
    private async void CancelClicked(object sender, RoutedEventArgs e) { if (model is not null) await model.CancelAsync(token); }
    private async void ContinueClicked(object sender, RoutedEventArgs e) { if (model is not null) await model.ContinueAsync(token); }

    private async void DocumentAction(bool print)
    {
        if (model is null || model.Lifetime.IsStopping) return;
        try
        {
            await model.Lifetime.RunAsync(_ =>
            {
                WriteDocument(print);
                return Task.CompletedTask;
            }, token);
        }
        catch (OperationCanceledException) { }
    }

    private void WriteDocument(bool print)
    {
        if (model?.Page != AuthenticationPage.Codes || model.Busy || model.Codes.Count != 10) return;
        try
        {
            if (print)
            {
                DocumentStatus.Text = RecoveryCodeDocument.Print(model.Codes) ? "Sent to the selected printer. Collect and secure the output." : "";
                return;
            }
            var dialog = new SaveFileDialog
            {
                Title = "Choose a private location for sensitive recovery codes",
                Filter = "Text document (*.txt)|*.txt", DefaultExt = ".txt",
                FileName = "GTS-SENSITIVE-recovery-codes.txt", AddExtension = true, OverwritePrompt = false
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var target = Path.GetFullPath(dialog.FileName);
            var dbDirectory = Path.GetDirectoryName(location!.GetAbsolutePath())!;
            if (Inside(target, dbDirectory) || Inside(target, AppContext.BaseDirectory) || InRepository(target))
            {
                DocumentStatus.Text = "Choose a private location outside the repository, application and database folders.";
                return;
            }
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using (var writer = new StreamWriter(stream, leaveOpen: true)) writer.Write(RecoveryCodeDocument.Format(model.Codes));
            stream.Flush(flushToDisk: true);
            DocumentStatus.Text = "Saved a sensitive plaintext document. Keep it private and offline.";
        }
        catch
        {
            SafeDiagnostics.Record("recovery-document-failed", "Warning");
            DocumentStatus.Text = "The operation failed. Choose a new filename or check the printer. If a partial file exists, handle it as sensitive.";
        }
    }

    private static bool Inside(string path, string directory) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool InRepository(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        for (var cursor = directory; cursor is not null; cursor = cursor.Parent)
        {
            // Reject redirected directories and worktrees as well as ordinary repositories.
            if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0 ||
                Directory.Exists(Path.Combine(cursor.FullName, ".git")) || File.Exists(Path.Combine(cursor.FullName, ".git")))
                return true;
        }
        return false;
    }
}
