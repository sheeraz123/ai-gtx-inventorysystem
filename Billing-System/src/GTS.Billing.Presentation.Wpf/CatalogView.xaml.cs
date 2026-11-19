using System.IO;
using System.Windows;
using System.Windows.Controls;
using GTS.Billing.Application;
using Microsoft.Win32;

namespace GTS.Billing.Presentation.Wpf;

public partial class CatalogView : UserControl
{
    public CatalogView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is CatalogViewModel previous) previous.ConfirmDuplicate=null;
            if (e.NewValue is CatalogViewModel current) current.ConfirmDuplicate=message =>
                MessageBox.Show(Window.GetWindow(this), message, "GTS Billing", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        };
    }

    internal bool ConfirmDiscard()
    {
        if (DataContext is not CatalogViewModel model) return true;
        if (model.Busy) return false;
        if (model.Dirty && MessageBox.Show(Window.GetWindow(this), "Discard unsaved catalog changes?", "GTS Billing",
             MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        model.Cancel();
        return true;
    }

    private void CloseEditor(object sender, RoutedEventArgs args) => ConfirmDiscard();
    private void RemoveImage(object sender, RoutedEventArgs args) => (DataContext as CatalogViewModel)?.SetImage(null);

    private void ChooseImage(object sender, RoutedEventArgs args)
    {
        if (DataContext is not CatalogViewModel { CanSave:true } model) return;
        var dialog=new OpenFileDialog { Title="Choose product image", Filter="PNG or JPEG|*.png;*.jpg;*.jpeg", CheckFileExists=true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            using var file=new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < 45 or > CompanyLogo.MaximumBytes) throw new InvalidDataException();
            var bytes=new byte[checked((int)file.Length)];
            file.ReadExactly(bytes);
            model.SetImage(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { model.Message="Choose a readable PNG or JPEG no larger than 1 MiB."; }
    }
}
