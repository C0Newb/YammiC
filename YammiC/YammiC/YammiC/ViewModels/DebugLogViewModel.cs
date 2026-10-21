using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>
/// The debug log on its own page. It only exists while the page is showing, and it changes the list only when a new line arrived,
/// so a busy log no longer slows the rest of the app (it used to be one big text block rebuilt on every update).
/// </summary>
public partial class DebugLogViewModel : BikeViewModelBase
{
    public ObservableCollection<string> Lines { get; } = new();

    public DebugLogViewModel(IBikeController controller) : base(controller)
    {
        Attach();
    }

    // Design-time constructor.
    public DebugLogViewModel() : this(new FakeBikeController())
    {
    }

    public string AllText => string.Join("\n", Lines);

    protected override void OnSnapshot(BikeSnapshot snapshot)
    {
        var logs = snapshot.Connection.Logs;
        if (logs.Count == 0)
        {
            if (Lines.Count > 0) Lines.Clear();
            return;
        }
        if (Lines.Count > 0 && Lines[^1] == logs[^1]) return;

        // Usually only new lines were added at the end: append just those.
        var known = Lines.Count == 0 ? -1 : logs.ToList().LastIndexOf(Lines[^1]);
        if (known >= 0 && known < logs.Count - 1 && logs.Count - known - 1 < logs.Count)
        {
            for (var i = known + 1; i < logs.Count; i++) Lines.Add(logs[i]);
            while (Lines.Count > 400) Lines.RemoveAt(0);
            return;
        }

        Lines.Clear();
        foreach (var line in logs) Lines.Add(line);
    }

    [RelayCommand]
    private void Clear() => Controller.ClearLogs();
}
