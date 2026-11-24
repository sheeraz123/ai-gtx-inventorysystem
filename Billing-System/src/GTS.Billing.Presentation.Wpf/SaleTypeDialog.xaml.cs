using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class SaleTypeDialog : Window
{
    internal SaleTypeDialog(Window owner)
    {
        InitializeComponent();
        Owner=owner;
        BillingIcon.Set(Proceed,"_Continue","\uE72A");
    }

    internal SalesIntent? SelectedIntent {get;private set;}

    private void Accept()
    {
        if(Choices.SelectedIndex<0) return;
        SelectedIntent=Choices.SelectedIndex==0?SalesIntent.Local:SalesIntent.Interstate;
        DialogResult=true;
    }

    private void ContinueClicked(object sender,RoutedEventArgs args)=>Accept();
    private void CloseClicked(object sender,RoutedEventArgs args)=>DialogResult=false;

    private void ChoiceKeyDown(object sender,KeyEventArgs args)
    {
        if(args.Key!=Key.Enter || Choices.SelectedIndex<0) return;
        args.Handled=true;
        Accept();
    }

    private void ChoiceDoubleClicked(object sender,MouseButtonEventArgs args)
    {
        if(args.ChangedButton!=MouseButton.Left || args.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(Choices,source) is not ListBoxItem {IsSelected:true}) return;
        args.Handled=true;
        Accept();
    }
}
