using System.Windows;
using System.Windows.Controls;

namespace GTS.Billing.Presentation.Wpf;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged+=(_,e)=>
        {
            if(e.OldValue is SettingsViewModel old) old.Confirm=null;
            if(e.NewValue is SettingsViewModel next)
                next.Confirm=message=>MessageBox.Show(Window.GetWindow(this),message,"GTS Billing",
                    MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
        };
    }

    private async void CompanyPresentationExpanded(object sender,RoutedEventArgs args)
    {
        if(args.OriginalSource==sender && DataContext is SettingsViewModel model)
            await model.OpenCompanyPresentationAsync();
    }
}
