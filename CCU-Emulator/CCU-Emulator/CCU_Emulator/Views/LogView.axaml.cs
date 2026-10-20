using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CCU_Emulator.ViewModels;

namespace CCU_Emulator.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        // Keep the newest entry in view.
        LogList.PropertyChanged += (_, e) =>
        {
            if (e.Property == ItemsControl.ItemCountProperty && LogList.ItemCount > 0)
                LogList.ScrollIntoView(LogList.ItemCount - 1);
        };
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.ExportText());
    }
}
