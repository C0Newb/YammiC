using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class TrackEditorView : ContentPage
{
    private readonly DispatcherTimer _position = new() { Interval = TimeSpan.FromSeconds(1) };
    private TrackEditorViewModel? _vm;

    public TrackEditorView()
    {
        InitializeComponent();
        _position.Tick += (_, _) => ShowPosition();
        Map.LinesChanged += OnLinesChanged;
        Map.SelectionChanged += () => _vm?.OnMapSelected(Map.SelectedLine);
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.MapChanged -= SyncMap;
                _vm.SelectionRequested -= SyncSelection;
            }
            _vm = DataContext as TrackEditorViewModel;
            if (_vm == null) return;

            Map.Lines = _vm.Lines;
            Map.Center = _vm.InitialCenter ?? Map.Center;
            _vm.MapChanged += SyncMap;
            _vm.SelectionRequested += SyncSelection;
            SyncMap();
            ShowPosition();
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _position.Start();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _position.Stop();
        _vm?.Detach();
    }

    // The view model owns the line list and the active line; the control draws them and edits the points.
    private void SyncMap()
    {
        if (_vm == null) return;
        Map.Lines = _vm.Lines;
        Map.ActiveLine = _vm.ActiveLine;
        Map.InvalidateVisual();
    }

    private void SyncSelection() => Map.SelectedLine = _vm?.Selected;

    private void ShowPosition()
    {
        var fix = _vm?.Fix;
        if (fix != null)
        {
            Map.Here = fix.Point;
            Map.HereBearing = fix.SpeedMps > 1.5 ? fix.Bearing : null;
        }
        else
        {
            Map.Here = null;
            Map.HereBearing = null;
        }
        Map.InvalidateVisual();
    }

    private void OnLinesChanged()
    {
        if (_vm == null) return;
        // A line the control just completed is no longer the active one.
        if (Map.ActiveLine == null && _vm.ActiveLine != null)
        {
            _vm.ActiveLine = null;
            _vm.Status = "Drag a handle to adjust a line. Tap Save when done.";
        }
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => Map.ZoomBy(1);

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => Map.ZoomBy(-1);

    private void Here_Click(object? sender, RoutedEventArgs e)
    {
        ShowPosition();
        var here = _vm?.Here;
        if (here != null) Map.Center = here.Value;
        else if (_vm != null) _vm.Status = "No GPS position yet.";
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (_vm.Save() && Navigation != null) await Navigation.PopAsync();
    }
}
