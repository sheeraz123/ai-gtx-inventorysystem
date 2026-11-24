using System.Windows;
using System.Windows.Controls;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class CustomerView : UserControl
{
    public CustomerView()
    {
        InitializeComponent();
        DataContextChanged+=(_,e)=>
        {
            if(e.OldValue is CustomerViewModel oldModel) oldModel.ConfirmShippingReplacement=null;
            if(e.NewValue is CustomerViewModel newModel) newModel.ConfirmShippingReplacement=()=>
                MessageBox.Show(Window.GetWindow(this),"Replace the selected shipping address with a copy of the billing address?",
                    "Copy billing address",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
            if(e.OldValue is CustomerViewModel previous) previous.ConfirmDuplicates=null;
            if(e.NewValue is CustomerViewModel current) current.ConfirmDuplicates=warnings=>MessageBox.Show(
                "Similar customers already exist in this company:\n\n"+
                string.Join("\n",warnings.Select(x=>x.Code+" — "+x.Name+" ("+string.Join(", ",x.Reasons)+")"))+
                "\n\nSave a separate customer anyway? No records will be merged.","GTS Billing — possible duplicate",
                MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
        };
    }
    internal bool ConfirmDiscard()
    {
        if(DataContext is not CustomerViewModel vm) return true;
        if(vm.Busy) return false;
        if(vm.Dirty&&MessageBox.Show("Discard unsaved Customer Master changes?","GTS Billing",
            MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes) return false;
        vm.CancelEditor();return true;
    }
    private void CancelEditor(object sender,RoutedEventArgs e)=>ConfirmDiscard();
    private void OpenCompanyMaster(object sender,RoutedEventArgs e)
    {
        if(Window.GetWindow(this)?.DataContext is ShellViewModel shell)
            shell.NavigateCommand.Execute(shell.Modules.Single(x=>x.Code=="company"));
    }
}

