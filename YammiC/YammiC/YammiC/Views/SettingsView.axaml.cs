using Avalonia.Controls;
using Avalonia.Input;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class SettingsView : ContentPage
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void PairingRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new PairingView { DataContext = new PairingViewModel(App.Controller) });
    }
}
