using Avalonia.Controls;
using Avalonia.Interactivity;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class DashboardView : ContentPage
{
    public DashboardView()
    {
        InitializeComponent();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        (DataContext as DashboardViewModel)?.Detach();
    }
}
