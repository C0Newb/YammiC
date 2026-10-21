using Avalonia.Controls;
using Avalonia.Interactivity;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class RideModeEditorView : ContentPage
{
    public RideModeEditorView()
    {
        InitializeComponent();
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RideModeEditorViewModel vm && vm.Save() && Navigation != null)
            await Navigation.PopAsync();
    }
}
