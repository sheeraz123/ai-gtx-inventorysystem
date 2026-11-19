using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

public partial class SecurityAdministrationView : UserControl
{
    public SecurityAdministrationView()
    {
        InitializeComponent();
        DataContextChanged+=(_,e)=>
        {
            if (e.OldValue is SecurityAdministrationViewModel old) old.PropertyChanged-=ClearPasswordsOnTransition;
            if (e.NewValue is SecurityAdministrationViewModel model)
            {
                model.ConfirmDiscard=()=>Confirm("Discard unsaved security changes?");
                model.ConfirmChange=Confirm;
                model.PropertyChanged+=ClearPasswordsOnTransition;
            }
        };
    }

    internal bool CanLeave()=>DataContext is not SecurityAdministrationViewModel model || model.CanLeave();

    private bool Confirm(string message)=>MessageBox.Show(Window.GetWindow(this),message,"GTS Billing",
        MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;

    private void ClearPasswordsOnTransition(object? sender,PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SecurityAdministrationViewModel.EditorOpen) or nameof(SecurityAdministrationViewModel.Editing)
            or nameof(SecurityAdministrationViewModel.Resetting))
        {
            TemporaryPassword.Clear();
            Confirmation.Clear();
        }
    }

    private async void Save(object sender,RoutedEventArgs e)
    {
        if (DataContext is not SecurityAdministrationViewModel { CanWrite:true } model) return;
        var secret=model.NeedsPassword?new PasswordInput(TemporaryPassword.Password,Confirmation.Password):null;
        TemporaryPassword.Clear();
        Confirmation.Clear();
        await model.SaveAsync(secret);
    }

    private void AuditDateChanged(object? sender,SelectionChangedEventArgs e) =>
        (DataContext as SecurityAdministrationViewModel)?.AuditFilterChanged();
    private void AuditTextChanged(object sender,TextChangedEventArgs e) =>
        (DataContext as SecurityAdministrationViewModel)?.AuditFilterChanged();
}
