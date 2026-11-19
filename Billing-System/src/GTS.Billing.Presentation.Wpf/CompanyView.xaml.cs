using System.Windows;
using System.Windows.Controls;

namespace GTS.Billing.Presentation.Wpf;

public partial class CompanyView : UserControl
{
    private bool filteringStates;
    private void StateSearchChanged(object sender,TextChangedEventArgs e)
    {
        if(sender is ComboBox {IsKeyboardFocusWithin:true} box) UpdateStateSearch(box);
    }
    internal void UpdateStateSearch(ComboBox box)
    {
        if(filteringStates || box.DataContext is not CompanyField {IsSearchable:true} field || field.IsFiltering) return;
        filteringStates=true;
        try
        {
            var text=box.Text;
            if(box.SelectedItem is CompanyChoice selected && text==selected.Label)
            {
                field.FilterChoices("");
                box.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,selected.Key);
                box.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.UpdateSource();
            }
            else
            {
                box.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,null);
                field.FilterChoices(text);
                box.IsDropDownOpen=true;
                box.SetCurrentValue(ComboBox.TextProperty,text);
                if(box.Template.FindName("PART_EditableTextBox",box) is TextBox edit) edit.CaretIndex=text.Length;
            }
        }
        finally {filteringStates=false;}
    }
    private bool restoringTab;
    public CompanyView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is CompanyViewModel before) before.PropertyChanged -= ModelChanged;
            if (e.NewValue is CompanyViewModel after) after.PropertyChanged += ModelChanged;
        };
    }

    private CompanyViewModel? Model => DataContext as CompanyViewModel;

    private async void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompanyViewModel.PresentationEditor))
            await LoadPresentationAsync();
    }

    private async Task LoadPresentationAsync()
    {
        if (EditorTabs.SelectedItem is not CompanySection { IsPresentation:true } ||
            Model?.PresentationEditor is not { } editor) return;
        var owner=Window.GetWindow(this);
        editor.ConfirmDiscard=() => Confirm("Discard unsaved changes?");
        editor.ConfirmRemoveLogo=() => MessageBox.Show(owner,
            "Remove this unused logo from the active catalog? The stored image will be retained.", "GTS Billing",
            MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
        await editor.InitializeAsync();
    }

    private async void EditorTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringTab || e.OriginalSource!=EditorTabs || Model?.EditorVisible!=true) return;
        if (e.RemovedItems.Count>0 && e.RemovedItems[0] is CompanySection { IsPresentation:true } prior &&
            Model.PresentationEditor is { } editor)
        {
            if (editor.Busy || editor.HasPendingChanges && !Confirm("Discard unsaved changes on this tab?"))
            {
                restoringTab=true;
                EditorTabs.SelectedItem=prior;
                restoringTab=false;
                return;
            }
            editor.DiscardEditor();
        }
        await LoadPresentationAsync();
    }

    private bool Confirm(string message) => MessageBox.Show(Window.GetWindow(this),message,"GTS Billing",
        MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;

    internal bool ConfirmDiscard()
    {
        if (Model?.Busy==true || Model?.PresentationEditor?.Busy==true) return false;
        if (Model?.HasUnsavedChanges==true && !Confirm("Discard unsaved Company Master changes?")) return false;
        Model?.CancelEditor();
        return true;
    }

    private async void CancelEditor(object sender,RoutedEventArgs e)
    {
        if (Model is { } model && ConfirmDiscard()) await model.RefreshAsync();
    }
}
