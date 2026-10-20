using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CCU_Emulator.ViewModels;

namespace CCU_Emulator.Views;

public partial class DashView : UserControl
{
    private DashViewModel? _vm;

    public DashView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as DashViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
        ShowImage();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The VM raises an empty name for "everything changed", so the image is refreshed on any state change
        // that touches it; decoding is cheap relative to how rarely a VPB image changes.
        if (e.PropertyName is null or "" or nameof(DashViewModel.VpbImageBytes)) ShowImage();
    }

    private byte[]? _shown;

    private void ShowImage()
    {
        var bytes = _vm?.VpbImageBytes;
        if (ReferenceEquals(bytes, _shown)) return;
        _shown = bytes;

        VpbError.IsVisible = false;
        if (bytes is null)
        {
            VpbImage.Source = null;
            return;
        }

        // Review Focus 5: an undecodable image must show a message, never crash the dash.
        try
        {
            using var stream = new MemoryStream(bytes);
            VpbImage.Source = new Bitmap(stream);
        }
        catch (Exception e)
        {
            VpbImage.Source = null;
            VpbError.Text = $"Could not decode the VPB image ({bytes.Length} bytes): {e.Message}";
            VpbError.IsVisible = true;
        }
    }
}
