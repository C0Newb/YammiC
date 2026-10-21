using CNCO.YccuSdk.Imaging;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Screens;

namespace CNCO.YccuSdk.Reels;

/// <summary>
/// Reels Mode: plays the videos of the chosen folder on the navigation screen, one at a time and looping, with the sound
/// on the phone. <see cref="Next"/> and <see cref="Previous"/> scroll between them (the bike's zoom buttons).
/// </summary>
public sealed class ReelsController
{
    private readonly IReelsPlatform _platform;
    private readonly NavigationSdk _navigation;
    private readonly Func<string?> _folder;
    private readonly Func<NavRefreshProfile> _profile;
    private readonly Logger _log;
    private readonly object _gate = new();

    private bool _running;
    private int _generation;
    private IReadOnlyList<ReelItem> _items = Array.Empty<ReelItem>();
    private int _index;

    public ReelsController(IReelsPlatform platform, NavigationSdk navigation, Func<string?> folder, Logger logger, Func<NavRefreshProfile>? profile = null)
    {
        _profile = profile ?? (() => NavRefreshProfile.Normal);
        _platform = platform;
        _navigation = navigation;
        _folder = folder;
        _log = logger.Section("Reels");
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _running;
        }
    }

    /// <summary>
    /// The video playing now, or null.
    /// </summary>
    public string? CurrentName
    {
        get
        {
            lock (_gate) return _running && _index < _items.Count ? _items[_index].Name : null;
        }
    }

    /// <summary>
    /// Lists the folder and starts playing from the first video.
    /// </summary>
    public async void Start()
    {
        int generation;
        lock (_gate)
        {
            _running = true;
            generation = ++_generation;
        }
        _platform.Stop();

        try
        {
            var folder = _folder();
            if (string.IsNullOrEmpty(folder))
            {
                ShowMessage("Choose a reels folder in Settings");
                return;
            }

            var items = await _platform.ListVideosAsync(folder).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_running || generation != _generation) return;
                _items = items;
                _index = 0;
            }

            if (items.Count == 0)
            {
                ShowMessage("No videos in the reels folder");
                return;
            }
            _log.Debug($"{items.Count} videos in the folder");
            PlayCurrent();
        }
        catch (Exception t)
        {
            _log.Debug($"start failed: {t.Message}");
            ShowMessage("Can't open the reels folder");
        }
    }

    /// <summary>
    /// Stops playback and the sound.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _generation++;
        }
        _platform.Stop();
    }

    /// <summary>
    /// Silences the video while the bike isn't showing the navigation screen. <see cref="Resume"/> starts the current one again.
    /// </summary>
    public void Suspend() => _platform.Stop();

    /// <summary>
    /// Plays the current video again after <see cref="Suspend"/> (from its start).
    /// </summary>
    public void Resume() => PlayCurrent();

    /// <summary>
    /// The refresh profile changed: start the current video again so it runs at the new rate (it begins from the start).
    /// </summary>
    public void RefreshProfile()
    {
        lock (_gate)
        {
            if (!_running || _items.Count == 0) return;
        }
        PlayCurrent();
    }

    /// <summary>
    /// Scrolls to the next video (wrapping round).
    /// </summary>
    public void Next() => Step(1);

    /// <summary>
    /// Scrolls to the previous video (wrapping round).
    /// </summary>
    public void Previous() => Step(-1);

    private void Step(int direction)
    {
        lock (_gate)
        {
            if (!_running || _items.Count == 0) return;
            _index = (_index + direction + _items.Count) % _items.Count;
        }
        PlayCurrent();
    }

    private void PlayCurrent()
    {
        ReelItem item;
        int generation;
        lock (_gate)
        {
            if (!_running || _index >= _items.Count) return;
            item = _items[_index];
            generation = _generation;
        }

        _log.Debug($"playing {item.Name}");
        _platform.Play(
            item,
            _profile().ReelsFps,
            bitmap =>
            {
                using (bitmap)
                {
                    lock (_gate)
                    {
                        if (!_running || generation != _generation) return;
                    }
                    if (!_navigation.ImagesWanted) return; // the bike is not on the navigation screen: skip the work
                    var size = _navigation.DisplaySize;
                    using var frame = YmcImaging.Letterbox(bitmap, size.Width, size.Height);
                    _navigation.PushImage(YmcImaging.EncodeJpeg(frame, _profile().ReelsJpegQuality));
                }
            },
            error =>
            {
                _log.Debug($"can't play {item.Name}: {error}");
                ShowMessage($"Can't play {item.Name}");
            });
    }

    /// <summary>
    /// Puts a line of text on the navigation screen (nothing is playing to show).
    /// </summary>
    private void ShowMessage(string text)
    {
        var layout = new ScreenLayout
        {
            Target = ScreenTarget.Navigation,
            Background = "#000000",
            Elements =
            {
                new ScreenElement
                {
                    Kind = ScreenElementKind.Text,
                    Text = text,
                    X = 20, Y = 80, Width = 440, Height = 80,
                    FontSize = 28, Bold = true, Align = ScreenTextAlign.Center, Color = "#FFFFFF",
                },
            },
        };
        var size = _navigation.DisplaySize;
        using var bitmap = ScreenRenderer.Render(layout, new ScreenContext(DateTime.Now, null, true), size.Width, size.Height);
        _navigation.PushImage(YmcImaging.EncodeJpeg(bitmap, 85));
    }
}
