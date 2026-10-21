using Avalonia.Controls;
using Avalonia.Interactivity;
using CNCO.YccuSdk.Tracks;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class TrackEditorView : ContentPage
{
    private TrackEditorViewModel? _vm;

    public TrackEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.MapChanged -= SyncMap;
            _vm = DataContext as TrackEditorViewModel;
            if (_vm == null) return;

            Map.Lines = _vm.Lines;
            Map.Here = _vm.Here;
            Map.Center = _vm.InitialCenter ?? Map.Center;
            _vm.MapChanged += SyncMap;
            Map.LinesChanged += OnLinesChanged;
            SyncMap();
        };
    }

    // The view model owns the line list and the active line; the control draws them and edits the points.
    private void SyncMap()
    {
        if (_vm == null) return;
        Map.Lines = _vm.Lines;
        Map.ActiveLine = _vm.ActiveLine;
        Map.InvalidateVisual();
    }

    private void OnLinesChanged()
    {
        // A line the control just completed is no longer the active one.
        if (_vm != null && Map.ActiveLine == null) _vm.ActiveLine = null;
        if (_vm != null && _vm.ActiveLine == null) _vm.Status = "Drag the round handles to adjust a line. Tap Save when done.";
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => Map.ZoomBy(1);

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => Map.ZoomBy(-1);

    private void Here_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Here is { } here)
        {
            Map.Here = here;
            Map.Center = here;
        }
        else if (_vm != null)
        {
            _vm.Status = "No GPS position yet.";
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (_vm.Save() && Navigation != null) await Navigation.PopAsync();
    }
}
