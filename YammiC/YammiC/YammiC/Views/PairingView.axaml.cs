using Avalonia.Controls;
using Avalonia.Interactivity;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class PairingView : ContentPage
{
    public PairingView()
    {
        InitializeComponent();
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PairingViewModel vm)
            vm.Save();
        if (Navigation != null)
            await Navigation.PopAsync();
    }
}
