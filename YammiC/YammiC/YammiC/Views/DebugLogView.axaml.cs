using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class DebugLogView : ContentPage
{
    public DebugLogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DebugLogViewModel vm)
                vm.Lines.CollectionChanged += OnLinesChanged;
        };
    }

    // Keep the newest line in view.
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is DebugLogViewModel { Lines.Count: > 0 } vm) LogList.ScrollIntoView(vm.Lines.Count - 1);
        }, DispatcherPriority.Background);
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DebugLogViewModel vm) return;
        // Selected lines if any, otherwise the whole log.
        var selected = LogList.SelectedItems;
        var text = selected is { Count: > 0 } ? string.Join("\n", selected.Cast<string>()) : vm.AllText;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (DataContext is DebugLogViewModel vm)
        {
            vm.Lines.CollectionChanged -= OnLinesChanged;
            vm.Detach();
        }
    }
}
