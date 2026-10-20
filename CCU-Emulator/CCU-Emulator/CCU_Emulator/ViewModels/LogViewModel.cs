using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using CCU_Emulator.Emulation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

public sealed partial class LogViewModel : ViewModelBase
{
    private readonly PacketLog _log;

    [ObservableProperty] public partial string FilterText { get; set; } = "";

    public ObservableCollection<PacketLogEntry> Visible { get; } = [];

    public LogViewModel(PacketLog log)
    {
        _log = log;
        log.Entries.CollectionChanged += OnEntriesChanged;
        Rebuild();
    }

    partial void OnFilterTextChanged(string value) => Rebuild();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { } added)
        {
            foreach (PacketLogEntry entry in added)
                if (Matches(entry)) Visible.Add(entry);
        }
        else
        {
            Rebuild();   // trimmed or cleared
        }
    }

    private bool Matches(PacketLogEntry e)
        => FilterText.Length == 0
           || e.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
           || e.Channel.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
           || e.Detail.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
           || e.Hex.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

    private void Rebuild()
    {
        Visible.Clear();
        foreach (var entry in _log.Entries)
            if (Matches(entry)) Visible.Add(entry);
    }

    [RelayCommand]
    private void Clear() => _log.Clear();

    public string ExportText()
    {
        var sb = new StringBuilder();
        foreach (var e in Visible)
            sb.Append(e.Time.ToString("HH:mm:ss.fff")).Append(' ').Append(e.Direction.ToString().PadRight(4)).Append(' ')
              .Append(e.Channel.PadRight(12)).Append(' ').Append(e.Name).Append(' ').Append(e.Hex).Append(' ').Append(e.Detail).Append('\n');
        return sb.ToString();
    }
}
