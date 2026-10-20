using Avalonia;
using Avalonia.Controls;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class MainView : NavigationPage
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (CurrentPage == null)
            await PushAsync(new DashboardView
            {
                DataContext = new DashboardViewModel(App.Controller)
            });
    }
}
