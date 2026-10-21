namespace CNCO.YccuSdk.Screens;

/// <summary>
/// The phone's music player, as far as screen actions need it.
/// </summary>
public interface IScreenMediaActions
{
    void PlayPause();
    void Next();
    void Previous();
}

/// <summary>
/// The live state of one screen: which page is showing, the flags and variables actions have set, and what the bike's
/// buttons do (the page's action for an event, else the document's default). Thread-safe.
/// </summary>
public sealed class ScreenController
{
    private readonly object _gate = new();
    private readonly IScreenMediaActions? _media;
    private readonly Dictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);
    private ScreenDocument _document;
    private int _index;

    public ScreenController(ScreenDocument document, IScreenMediaActions? media = null)
    {
        _document = document;
        _media = media;
    }

    /// <summary>
    /// Raised after a page, flag or variable changed (the screen needs redrawing).
    /// </summary>
    public event Action? Changed;

    public ScreenDocument Document
    {
        get
        {
            lock (_gate) return _document;
        }
    }

    public ScreenPage CurrentPage
    {
        get
        {
            lock (_gate) return _document.Pages[_index];
        }
    }

    public int PageIndex
    {
        get
        {
            lock (_gate) return _index;
        }
    }

    public int PageCount
    {
        get
        {
            lock (_gate) return _document.Pages.Count;
        }
    }

    /// <summary>
    /// Swaps in an edited document. Stays on the same page if it still exists, else the first.
    /// </summary>
    public void SetDocument(ScreenDocument document)
    {
        lock (_gate)
        {
            var currentId = _document.Pages[_index].Id;
            _document = document;
            var found = document.Pages.FindIndex(p => p.Id == currentId);
            _index = found < 0 ? 0 : found;
        }
        Changed?.Invoke();
    }

    public void GoTo(string pageId)
    {
        lock (_gate)
        {
            var found = _document.Pages.FindIndex(p => p.Id == pageId);
            if (found < 0 || found == _index) return;
            _index = found;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Runs the action for <paramref name="screenEvent"/>. False when there is no action at all (neither on the page nor a
    /// document default), so the caller can fall back to something else; true otherwise, even for "None" or an action that
    /// had nothing to do.
    /// </summary>
    public bool Handle(ScreenEvent screenEvent)
    {
        ScreenAction action;
        lock (_gate)
        {
            var page = _document.Pages[_index];
            var text = page.Actions.TryGetValue(screenEvent, out var own) ? own
                : _document.DefaultActions.TryGetValue(screenEvent, out var fallback) ? fallback
                : null;
            if (!ScreenAction.TryParse(text, out action)) return false;
        }

        Run(action);
        return true;
    }

    private void Run(ScreenAction action)
    {
        var changed = false;
        lock (_gate)
        {
            var count = _document.Pages.Count;
            switch (action.Kind)
            {
                case ScreenActionKind.NextPage when count > 1:
                    _index = (_index + 1) % count;
                    changed = true;
                    break;
                case ScreenActionKind.PreviousPage when count > 1:
                    _index = (_index - 1 + count) % count;
                    changed = true;
                    break;
                case ScreenActionKind.GoTo:
                    var found = _document.Pages.FindIndex(p => p.Id == action.Arg);
                    if (found >= 0 && found != _index)
                    {
                        _index = found;
                        changed = true;
                    }
                    break;
                case ScreenActionKind.Toggle:
                    _flags[action.Arg] = !(_flags.TryGetValue(action.Arg, out var on) && on);
                    changed = true;
                    break;
                case ScreenActionKind.Set:
                    _vars[action.Arg] = action.Value;
                    changed = true;
                    break;
            }
        }

        // Media goes out of the lock: it talks to the phone.
        switch (action.Kind)
        {
            case ScreenActionKind.MediaPlayPause: _media?.PlayPause(); break;
            case ScreenActionKind.MediaNext: _media?.Next(); break;
            case ScreenActionKind.MediaPrevious: _media?.Previous(); break;
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// The road name for the page showing now, with its {variables} filled in: the page's own text, else the document's
    /// default, else empty.
    /// </summary>
    public string RoadNameFor(ScreenContext context)
    {
        string? template;
        lock (_gate) template = _document.Pages[_index].RoadName ?? _document.DefaultRoadName;
        return string.IsNullOrEmpty(template) ? "" : ScreenVariables.Resolve(template, context);
    }

    public ScreenSnapshot Snapshot(ScreenContext context)
    {
        lock (_gate)
        {
            return new ScreenSnapshot(
                context,
                _document.Pages[_index].Id,
                _index,
                _document.Pages.Count,
                new Dictionary<string, bool>(_flags, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(_vars, StringComparer.OrdinalIgnoreCase));
        }
    }
}
