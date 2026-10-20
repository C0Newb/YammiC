using CCU_Emulator.Transport;

namespace CCU_Emulator.Emulation;

/// <summary>Owns the settings, the chosen transport and the session, and drives the session clock.</summary>
public sealed class EmulatorHost : IAsyncDisposable
{
    private readonly Action<Action> _post;
    private CancellationTokenSource? _tickCts;

    public string SettingsPath { get; }
    public CcuSettings Settings { get; }
    public ICcuTransport Transport { get; }
    public CcuSession Session { get; }
    public bool IsRunning => Transport.IsRunning;

    /// <summary>Why the last <see cref="StartAsync"/> did not start (null when it did).</summary>
    public string? LastError { get; private set; }

    public EmulatorHost(
        CcuSettings settings,
        string settingsPath,
        Action<Action>? post = null,
        Func<TransportKind, ICcuTransport>? transportFactory = null)
    {
        Settings = settings;
        SettingsPath = settingsPath;
        _post = post ?? (a => a());
        Transport = (transportFactory ?? TransportFactory.Create)(settings.Transport);
        Session = new CcuSession(Transport, settings, _post);
    }

    public async Task StartAsync()
    {
        if (_tickCts is not null) return;   // already running
        LastError = null;
        var errors = Settings.Validate();
        if (errors.Count > 0)
        {
            LastError = string.Join(" ", errors);
            _post(() => Session.Log.Info("Host", "not started: " + LastError));
            return;
        }

        try
        {
            await Transport.StartAsync(Settings);
        }
        catch (Exception e)
        {
            LastError = e.Message;
            _post(() => Session.Log.Info("Host", "transport failed to start: " + e.Message));
            return;
        }

        _tickCts = new CancellationTokenSource();
        _ = TickLoopAsync(_tickCts.Token);
        _post(() => Session.Log.Info("Host", $"started as {Settings.AdvertisedName} on {Settings.Transport}"));
    }

    public async Task StopAsync()
    {
        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _tickCts = null;
        await Transport.StopAsync();
        _post(() => Session.Log.Info("Host", "stopped"));
    }

    public void SaveSettings() => SettingsStore.Save(SettingsPath, Settings);

    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                _post(() => Session.Tick(TimeSpan.FromSeconds(1)));
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await Transport.DisposeAsync();
    }
}
