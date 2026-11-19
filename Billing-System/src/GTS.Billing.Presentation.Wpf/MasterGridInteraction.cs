using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GTS.Billing.Presentation.Wpf;

public static class MasterGridInteraction
{
    public static readonly DependencyProperty ViewRowCommandProperty = DependencyProperty.RegisterAttached(
        "ViewRowCommand", typeof(ICommand), typeof(MasterGridInteraction), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetViewRowCommand(DependencyObject target) => (ICommand?)target.GetValue(ViewRowCommandProperty);
    public static void SetViewRowCommand(DependencyObject target, ICommand? value) => target.SetValue(ViewRowCommandProperty, value);

    private static void OnCommandChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DataGrid grid) return;
        grid.MouseDoubleClick -= OpenRow;
        if (args.NewValue is ICommand) grid.MouseDoubleClick += OpenRow;
    }

    private static void OpenRow(object sender, MouseButtonEventArgs args)
    {
        if (sender is not DataGrid grid || args.ChangedButton != MouseButton.Left ||
            args.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(grid, source) is not DataGridRow { Item: { } item }) return;
        grid.SelectedItem = item;
        var command = GetViewRowCommand(grid);
        if (command?.CanExecute(null) != true) return;
        args.Handled = true;
        command.Execute(null);
    }
}

internal sealed class MasterSearch
{
    private CancellationTokenSource? pending;

    public static string Query(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        return trimmed.Count(c=>!char.IsWhiteSpace(c)) >= 3 ? trimmed : "";
    }

    public void Cancel()
    {
        var previous = pending;
        pending = null;
        previous?.Cancel();
    }

    public async Task ScheduleAsync(bool debounce, Func<bool> busy, Func<CancellationToken, Task> load)
    {
        Cancel();
        using var source = new CancellationTokenSource();
        pending = source;
        try
        {
            if (debounce) await Task.Delay(SettingsDisplay.Current.Get(GTS.Billing.Application.SettingsRegistry.SearchDebounce), source.Token);
            while (busy()) await Task.Delay(30, source.Token);
            source.Token.ThrowIfCancellationRequested();
            await load(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        finally
        {
            if (pending == source) pending = null;
        }
    }
}
