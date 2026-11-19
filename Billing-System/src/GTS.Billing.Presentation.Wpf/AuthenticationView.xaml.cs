using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private LoginPresentation branding=LoginPresentation.Default;

    public AuthenticationView()
    {
        InitializeComponent();
        Username.TextChanged+=(_,_)=>{UsernameError.Text="";UpdateHints();};
        PasswordField.PasswordChanged+=(_,_)=>{PasswordError.Text="";UpdateHints();};
        VisiblePassword.TextChanged+=(_,_)=>{PasswordError.Text="";UpdateHints();};
        ApplyLoginPresentation(branding);
    }

    internal void ApplyLoginPresentation(LoginPresentation value)
    {
        branding=value;
        if(model is not null) model.LoginPresentation=value;
        BrandName.Text=value.Get("ApplicationName");
        void Optional(TextBlock block,string key)
        {
            block.Text=value.Get(key);
            block.Visibility=Visible(block.Text.Length>0);
        }
        Optional(ClientName,"ClientName");
        Optional(WelcomeText,"Welcome");
        Optional(Footer,"Footer");
        Optional(Copyright,"Copyright");
        var primary=Paint(value.Get("Primary"));
        var secondary=Paint(value.Get("Secondary"));
        BrandHeader.Background=primary;
        BrandName.Foreground=ClientName.Foreground=Contrast(primary.Color);
        Submit.Background=secondary;
        Submit.Foreground=Contrast(secondary.Color);
        BrandLogo.Source=Image(value.Logo);
        BrandLogo.Visibility=Visible(BrandLogo.Source is not null);
        if(Image(value.Background) is { } background)
            Backdrop.Background=new ImageBrush(background) {Stretch=Stretch.UniformToFill,Opacity=.28};
        else Backdrop.SetResourceReference(Control.BackgroundProperty,"Palette.Canvas");
        UsernameLabel.Content=value.Get("Username");
        UsernameHint.Text=value.Get("UsernameHint");
        PasswordHint.Text=value.Get("PasswordHint");
        Reveal.Content=value.Get("Reveal");
        Recovery.Content=value.Get("Recovery");
        System.Windows.Automation.AutomationProperties.SetName(Username,value.Get("Username"));
        System.Windows.Automation.AutomationProperties.SetName(PasswordField,value.Get("Password"));
        if(model is not null) Refresh();
    }

    internal void ShowDesignPreview(LoginPresentation value)
    {
        ApplyLoginPresentation(value);
        Heading.Text=value.Get("Title");
        Instructions.Text=value.Get("Subtitle");
        PasswordLabel.Content=value.Get("Password");
        Submit.Content=value.Get("Submit");
        CodeRow.Visibility=ConfirmationRow.Visibility=Cancel.Visibility=CodePanel.Visibility=Visibility.Collapsed;
        Form.IsEnabled=false;
    }

    private static SolidColorBrush Paint(string value)=>new((Color)ColorConverter.ConvertFromString(value));
    private static Brush Contrast(Color color)
    {
        static double Linear(byte channel)
        {
            var value=channel/255d;
            return value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4);
        }
        return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B)>.179?Brushes.Black:Brushes.White;
    }
    private static BitmapImage? Image(byte[]? bytes)
    {
        if(bytes is not {Length:>0}) return null;
        try
        {
            using var stream=new MemoryStream(bytes,false);
            var image=new BitmapImage();
            image.BeginInit();
            image.CacheOption=BitmapCacheOption.OnLoad;
            image.StreamSource=stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch(Exception e) when(e is IOException or ArgumentException or NotSupportedException or FormatException) {return null;}
    }

    internal void Attach(AuthenticationViewModel vm, IDatabaseLocation databaseLocation, CancellationToken ct)
    {
        model = vm; location = databaseLocation; token = ct;
        vm.LoginPresentation=branding;
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
                else if (page is AuthenticationPage.Recover or AuthenticationPage.ChangePassword) Code.Focus();
                else if (page == AuthenticationPage.Regenerate) PasswordField.Focus();
                else if (page == AuthenticationPage.Codes) Continue.Focus();
            }), DispatcherPriority.Input);
        }
        Heading.Text = page switch
        {
            AuthenticationPage.Setup => "Set up your Super Admin",
            AuthenticationPage.Login => branding.Get("Title"),
            AuthenticationPage.Recover => "Recover the Super Admin account",
            AuthenticationPage.Regenerate => "Generate new recovery codes",
            AuthenticationPage.ChangePassword => "Change your password",
            AuthenticationPage.Codes => "Save your ten recovery codes now",
            AuthenticationPage.Closing => "Closing GTS Billing, please wait…",
            _ => "No development database configured"
        };
        Instructions.Text = page switch
        {
            AuthenticationPage.Setup => "Use a username of 3–40 ASCII letters, digits, dots, underscores or hyphens. Choose a passphrase of at least 15 characters.",
            AuthenticationPage.Login => branding.Get("Subtitle"),
            AuthenticationPage.Recover => "Enter an unused Super Admin code and a new passphrase. No built-in bypass exists if you lose the password and all codes. Codes do not decrypt a database.",
            AuthenticationPage.Regenerate => "Enter your current password. All previous unused codes will be revoked when the new set commits.",
            AuthenticationPage.ChangePassword => model.PasswordChangeRequired
                ? "Replace your temporary password before using Billing. Use a new passphrase of at least 15 characters."
                : "Enter your current password and a new passphrase. You will sign in again after saving.",
            AuthenticationPage.Codes => "Sensitive secrets. Print or save them to a private location. These codes cannot be shown again. Continue discards this display.",
            AuthenticationPage.Closing => model.ClosingMessage,
            _ => "Explicitly initialize or configure a synthetic development database. The billing shell is locked."
        };
        Form.Visibility = Visible(page is AuthenticationPage.Setup or AuthenticationPage.Login or AuthenticationPage.Recover or AuthenticationPage.Regenerate or AuthenticationPage.ChangePassword);
        UsernameRow.Visibility = Visible(page is AuthenticationPage.Setup or AuthenticationPage.Login);
        CodeRow.Visibility = Visible(page is AuthenticationPage.Recover or AuthenticationPage.ChangePassword);
        CodeLabel.Content=page==AuthenticationPage.ChangePassword ? "_Current password" : "Recovery _code";
        PasswordLabel.Content=page==AuthenticationPage.Login?branding.Get("Password"):
            page==AuthenticationPage.ChangePassword ? "_New password" : "_Password";
        System.Windows.Automation.AutomationProperties.SetName(Code,page==AuthenticationPage.ChangePassword ? "Current password" : "Recovery code");
        Code.MaxLength=page==AuthenticationPage.ChangePassword ? 512 : 64;
        ConfirmationRow.Visibility = Visible(page is AuthenticationPage.Setup or AuthenticationPage.Recover or AuthenticationPage.ChangePassword);
        Recovery.Visibility = Visible(page == AuthenticationPage.Login);
        Reveal.Visibility = Visible(page == AuthenticationPage.Login);
        Submit.Content = page==AuthenticationPage.Login ? branding.Get(model.Busy?"Busy":"Submit") : model.Busy?"Please wait…":"_Continue";
        UpdateHints();
        Cancel.Visibility = Visible(page is AuthenticationPage.Recover or AuthenticationPage.Regenerate ||
            page==AuthenticationPage.ChangePassword && !model.PasswordChangeRequired);
        CodePanel.Visibility = Visible(page == AuthenticationPage.Codes);
        CodeList.ItemsSource = page == AuthenticationPage.Codes ? model.Codes : null;
        Form.IsEnabled = CodePanel.IsEnabled = !model.Busy && !model.Lifetime.IsStopping;
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateHints()
    {
        var login=model is null || model.Page==AuthenticationPage.Login;
        UsernameHint.Visibility=Visible(login && Username.Text.Length==0);
        using var masked=PasswordField.SecurePassword;
        PasswordHint.Visibility=Visible(login && masked.Length==0 && VisiblePassword.Text.Length==0);
    }

    private async void SubmitClicked(object sender, RoutedEventArgs e)
    {
        if (model is null || model.Busy) return;
        var password=Reveal.IsChecked==true?VisiblePassword.Text:PasswordField.Password;
        if(model.Page==AuthenticationPage.Login)
        {
            UsernameError.Text=string.IsNullOrWhiteSpace(Username.Text)?branding.Get("RequiredUsername"):"";
            PasswordError.Text=password.Length==0?branding.Get("RequiredPassword"):"";
            if(UsernameError.Text.Length>0 || PasswordError.Text.Length>0)
            {
                (UsernameError.Text.Length>0?(Control)Username:Reveal.IsChecked==true?VisiblePassword:PasswordField).Focus();
                return;
            }
        }
        var task = model.SubmitAsync(Username.Text, password,
            Confirmation.Password, Code.Password, token);
        ClearInputs();
        await task;
    }

    private void ClearInputs()
    {
        VisiblePassword.Clear();
        PasswordField.Clear();
        Reveal.IsChecked=false;
        Confirmation.Clear();
        Code.Clear();
    }

    private void PasswordVisibilityChanged(object sender, RoutedEventArgs e)
    {
        var show=Reveal.IsChecked==true;
        if(show)
        {
            VisiblePassword.Text=PasswordField.Password;
            PasswordField.Clear();
        }
        else
        {
            PasswordField.Password=VisiblePassword.Text;
            VisiblePassword.Clear();
        }
        PasswordField.Visibility=Visible(!show);
        VisiblePassword.Visibility=Visible(show);
        PasswordLabel.Target=show?VisiblePassword:PasswordField;
        UpdateHints();
        if(show) VisiblePassword.Focus();
    }

    internal bool TryCancelFromKeyboard()
    {
        if(!IsVisible || !Cancel.IsVisible || model is null || model.Busy) return false;
        CancelClicked(Cancel,new RoutedEventArgs());
        return true;
    }
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
