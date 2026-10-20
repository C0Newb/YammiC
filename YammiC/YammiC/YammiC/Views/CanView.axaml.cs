using Avalonia.Controls;
using Avalonia.Interactivity;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class CanView : ContentPage
{
    public CanView()
    {
        InitializeComponent();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        (DataContext as CanViewModel)?.Detach();
    }
}
