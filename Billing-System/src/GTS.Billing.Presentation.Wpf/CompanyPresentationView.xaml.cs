using System.IO;
using System.Windows;
using System.Windows.Controls;
using GTS.Billing.Application;
using Microsoft.Win32;

namespace GTS.Billing.Presentation.Wpf;

public partial class CompanyPresentationView : UserControl
{
    public CompanyPresentationView() => InitializeComponent();

    private void ChooseImage(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CompanyPresentationViewModel { CanWriteRecord:true } model) return;
        var dialog = new OpenFileDialog { Title = "Choose a company logo", Filter = "PNG or JPEG|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            using var file = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < 45 or > CompanyLogo.MaximumBytes) throw new InvalidDataException();
            var bytes = new byte[checked((int)file.Length)];
            file.ReadExactly(bytes);
            model.ChooseImage(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.Message = "The image could not be read. Choose a PNG or JPEG no larger than 1 MiB.";
        }
    }
}
