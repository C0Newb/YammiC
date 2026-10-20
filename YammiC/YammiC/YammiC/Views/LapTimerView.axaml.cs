using Avalonia.Controls;
using Avalonia.Interactivity;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class LapTimerView : ContentPage
{
    public LapTimerView()
    {
        InitializeComponent();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        (DataContext as LapTimerViewModel)?.Detach();
    }
}
