using CNCO.YccuSdk.Handlers;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk;

public class YmcConnectionBroker
{
    private readonly Logger _log;
    private readonly IYmcPacketHandler _packetHandler;
    private readonly CancellationToken _lifetime;
    private readonly IYmcBluetoothConnectionManager _bluetooth;

    private readonly YmcBikeSessionStore _sessionStore;
    private readonly object _stateLock = new();
    private BikeDebugState _state;

    private readonly CcuGetDeviceLanguageRequestHandler _languageHandler = new(YmcLanguage.English);
    private readonly CcuVolumeControllableRequestHandler _volumeControllableHandler;
    private readonly CcuGetDeviceTimeRequestHandler _timeHandler = new();
    private readonly CcuPingRequestHandler _pingHandler = new();
    private readonly YmcVirtualPitBoardController _virtualPitBoardController;

    private readonly IYmcPhoneStatusProvider _phoneStatus;
    private YmcMessageBroker? _messageBroker;

    /// <param name="platform">Host-provided Bluetooth, phone and storage services.</param>
    /// <param name="packetHandler">Sends serialized packets over the right transport.</param>
    /// <param name="logger">Root logger.</param>
    /// <param name="logFileLabel">Label shown for where logs are written.</param>
    /// <param name="lifetime">Cancelled when the owner (e.g. a foreground service) shuts down.</param>
    public YmcConnectionBroker(
        YmcPlatform platform,
        IYmcPacketHandler packetHandler,
        Logger logger,
        string logFileLabel,
        CancellationToken lifetime = default)
    {
        _log = logger.Section("YmcConnectionBroker");
        _packetHandler = packetHandler;
        _lifetime = lifetime;
        _bluetooth = platform.Bluetooth;
        _phoneStatus = platform.PhoneStatus;

        _sessionStore = new YmcBikeSessionStore(platform.Storage);
        _state = _sessionStore.LoadState(logFileLabel);

        _bluetooth.LogMessage += PushLog;
        _bluetooth.QuietLogMessage += line => _log.Debug(line.Trim());
        _bluetooth.Disconnected += () =>
        {
            _messageBroker = null;
            StopConnectedJobs();
            PushState("Disconnected");
        };

        MessageHandler = new YmcMessageHandler(logger, packetHandler, _bluetooth.Notifications);

        _volumeControllableHandler = new CcuVolumeControllableRequestHandler(platform.Volume);
        _virtualPitBoardController = new YmcVirtualPitBoardController(logger);
    }

    /// <summary>
    /// Latest state snapshot.
    /// </summary>
    public BikeDebugState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }

    /// <summary>
    /// Raised (on whatever thread made the change) every time <see cref="State"/> changes.
    /// UI code must marshal to its own thread.
    /// </summary>
    public event Action<BikeDebugState>? StateChanged;

    public YmcMessageHandler MessageHandler { get; }

    /// <summary>
    /// Every GATT notification/indication from the bike, tagged with its characteristic.
    /// </summary>
    public YmcNotificationBus Notifications => _bluetooth.Notifications;

    public CcuGetDeviceLanguageRequestHandler LanguageHandler => _languageHandler;

    public YmcMessageBroker? MessageBroker => _messageBroker;

    public bool IsBikeConnected => _messageBroker != null;

    /// <summary>
    /// True while a connect attempt is in flight.
    /// </summary>
    public bool IsConnecting => _active;

    /// <summary>
    /// The bike's Bluetooth address from the last time it was found, or null if never.
    /// </summary>
    public string? BikeAddress => _sessionStore.BikeAddress;

    private sealed record PeriodicJob(TimeSpan Interval, Func<YmcMessageBroker, Task> Block);

    private readonly List<PeriodicJob> _periodicJobs = new();
    private CancellationTokenSource? _connectedJobsCancellation;
    private readonly List<Func<YmcMessageBroker, Task>> _onConnectJobs = new();

    public void AddConnectedJob(TimeSpan interval, Func<YmcMessageBroker, Task> block)
    {
        _periodicJobs.Add(new PeriodicJob(interval, block));
    }

    /// <summary>
    /// Registers <paramref name="block"/> to run once, immediately after every successful connect.
    /// </summary>
    public void AddOnConnectJob(Func<YmcMessageBroker, Task> block)
    {
        _onConnectJobs.Add(block);
    }

    private async Task RunOnConnectJobsAsync(YmcMessageBroker broker)
    {
        foreach (var job in _onConnectJobs)
        {
            try
            {
                await job(broker).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception t)
            {
                LogThrowable("onConnectJob", t);
            }
        }
    }

    private void StartConnectedJobs(YmcMessageBroker broker)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _connectedJobsCancellation = cancellation;
        var token = cancellation.Token;

        foreach (var job in _periodicJobs)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        try
                        {
                            await job.Block(broker).WaitAsync(token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception t)
                        {
                            LogThrowable("periodicJob", t);
                        }
                        await Task.Delay(job.Interval, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stopped.
                }
            });
        }
    }

    private void StopConnectedJobs()
    {
        _connectedJobsCancellation?.Cancel();
        _connectedJobsCancellation = null;
    }

    private Task? _scanTask;
    private CancellationTokenSource? _scanCancellation;
    private Task<YmcAuthentication?>? _sppCredentials;
    private volatile bool _active;

    // -- Public API ------------------------------------------------------------

    public void SetPermissionsGranted(bool granted)
    {
        UpdateState(s => s with { PermissionsGranted = granted });
    }

    public void UpdateCredentials(string ccuId, string passKey)
    {
        _sessionStore.SaveCredentials(ccuId, passKey);
        UpdateState(s => s with { CcuId = ccuId, PassKey = passKey });
    }

    public void UpdateDeviceName(string deviceName)
    {
        _sessionStore.SaveDeviceName(deviceName);
        UpdateState(s => s with { DeviceName = deviceName });
    }

    public void ClearLogs()
    {
        UpdateState(s => s with { Logs = Array.Empty<string>() });
    }

    public void LogThrowable(string tag, Exception throwable)
    {
        var stack = $"{tag}: {throwable.GetType().FullName}: {throwable.Message}\n{throwable.StackTrace}";
        PushError(stack);
    }

    /// <summary>
    /// Sends <paramref name="image"/> (already-encoded graphic bytes, e.g. JPEG) to the Virtual Pit Board.
    /// Returns false if not connected, the CCU rejected the request, or a chunk failed after retries.
    /// </summary>
    public async Task<bool> SendVirtualPitBoardMessageAsync(byte[] image)
    {
        var broker = _messageBroker;
        if (broker == null) return false;
        return await _virtualPitBoardController.SendMessageAsync(broker.PacketHandler, broker.ConnectionContext, image).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears the Virtual Pit Board display. Returns false if not connected or the CCU rejected it.
    /// </summary>
    public async Task<bool> ClearVirtualPitBoardAsync()
    {
        var broker = _messageBroker;
        if (broker == null) return false;
        return await _virtualPitBoardController.ClearAsync(broker.PacketHandler, broker.ConnectionContext).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        _bluetooth.AssertConnectPermission();
        _scanCancellation?.Cancel();
        _scanCancellation = null;
        _scanTask = null;
        _sppCredentials = null;
        _active = false;
        _messageBroker = null;
        StopConnectedJobs();
        MessageHandler.StopMessageHandler();
        _bluetooth.Disconnect();
        PushState("Disconnected");
    }

    public void ConnectAndSend()
    {
        if (_scanTask != null && !_scanTask.IsCompleted) return;

        _bluetooth.AssertConnectPermission();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _scanCancellation = cancellation;
        _scanTask = Task.Run(async () =>
        {
            PushLog("connectJob: started");
            try
            {
                await RunConnectAndSendAsync(cancellation.Token).ConfigureAwait(false);
                PushLog("connectJob: completed");
            }
            catch (Exception t)
            {
                PushLog($"connectJob: completed with {t.GetType().FullName}: {t.Message}");
                LogThrowable("connectJobCompletion", t);
            }
        });
    }

    // -- Connection lifecycle --------------------------------------------------

    private async Task RunConnectAndSendAsync(CancellationToken cancellationToken)
    {
        if (!_bluetooth.IsBluetoothEnabled)
        {
            PushError("Bluetooth is off");
            return;
        }

        var credentials = State.Credentials;
        var targetName = State.DeviceName.Trim();
        if (string.IsNullOrWhiteSpace(targetName)) targetName = $"YCCU_{credentials.CcuId}";

        try
        {
            _active = true;
            PushLog(
                $"Start connect: target={targetName} " +
                $"bonded={_bluetooth.BondedDeviceCount} " +
                $"permissions={State.PermissionsGranted}");

            _sppCredentials = _bluetooth.OpenSppServerAsync();

            PushState("Scanning");
            PushLog($"Scanning for {targetName}");
            var device = await _bluetooth.ScanForDeviceAsync(targetName, cancellationToken).ConfigureAwait(false);
            if (device == null)
            {
                // An expected outcome (bike off / out of range), not a fault: report it plainly, no stack trace.
                _sppCredentials = null;
                _bluetooth.Disconnect();
                PushError($"Couldn't find {targetName}. Make sure the bike is on and in range, then try again.");
                return;
            }

            if (!string.IsNullOrEmpty(device.Address)) _sessionStore.SaveBikeAddress(device.Address);
            PushState("Connecting");
            PushLog($"Connecting to {device.Name} / {device.Address} bondState={device.BondState}");
            await _bluetooth.ConnectAsync(device).ConfigureAwait(false);

            PushState("Discovering");
            PushLog("Negotiating MTU and discovering services");
            await _bluetooth.RequestMtuAsync(183).ConfigureAwait(false);
            await _bluetooth.DiscoverServicesAsync().ConfigureAwait(false);

            if (!_bluetooth.HasService(YmcBikeProtocol.ServiceUuid))
                throw new InvalidOperationException("Yamaha service not found");

            var connectionContext = new YmcConnectionContext(_bluetooth, YmcBikeProtocol.ServiceUuid);
            var localBroker = new YmcMessageBroker(_packetHandler, connectionContext, _phoneStatus);

            PushState("Subscribing");
            await _bluetooth.SubscribeToNotificationsAsync(YmcBikeProtocol.ServiceUuid).ConfigureAwait(false);

            MessageHandler.RegisterHandler(YmcCommandCode.PingRequest, (ph, d) => new CcuPingRequest(ph, d), _pingHandler);
            MessageHandler.RegisterHandler(YmcCommandCode.VehicleSettingRequest, (ph, d) => new CcuGetDeviceTimeRequest(ph, d), _timeHandler);
            MessageHandler.RegisterHandler(YmcCommandCode.LanguageRequest, (ph, d) => new CcuGetDeviceLanguageRequest(ph, d), _languageHandler);
            MessageHandler.RegisterHandler(
                YmcCommandCode.VolumeControllableRequest,
                (ph, d) => new CcuVolumeControllableRequest(ph, d),
                _volumeControllableHandler);
            _virtualPitBoardController.RegisterHandlers(MessageHandler);
            MessageHandler.StartMessageHandler(connectionContext);

            PushState("Listening");
            PushLog("Waiting for CCU startup notifications");
            await Task.Delay(TimeSpan.FromMilliseconds(2_000), cancellationToken).ConfigureAwait(false);

            PushState("Authenticating");
            PushLog("Authenticating");
            YmcAuthentication? sppCreds = null;
            if (_sppCredentials != null)
            {
                (_, sppCreds) = await _sppCredentials.WithTimeoutAsync(TimeSpan.FromMilliseconds(2_000)).ConfigureAwait(false);
            }
            var authCcuId = sppCreds?.CcuId ?? credentials.CcuId;
            var authPassKey = sppCreds?.PassKey ?? credentials.PassKey;
            PushLog($"Auth: using {(sppCreds != null ? "RFCOMM" : "stored")} credentials ccuId={authCcuId}");
            await localBroker.SendAuthenticationRequestAsync(authCcuId, authPassKey).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);

            PushState("Identifying");
            PushLog("Sending IdentifyAppRequest");
            await localBroker.SendIdentificationRequestAsync().ConfigureAwait(false);

            _messageBroker = localBroker;
            await RunOnConnectJobsAsync(localBroker).ConfigureAwait(false);
            StartConnectedJobs(localBroker);
            PushState("Done");
            PushLog("Connected!");
        }
        catch (OperationCanceledException e)
        {
            _messageBroker = null;
            StopConnectedJobs();
            LogThrowable("runConnectAndSend.cancelled", e);
        }
        catch (Exception t)
        {
            _messageBroker = null;
            StopConnectedJobs();
            LogThrowable("runConnectAndSend", t);
        }
        finally
        {
            _active = false;
        }
    }

    // -- Logging / state -------------------------------------------------------

    private void UpdateState(Func<BikeDebugState, BikeDebugState> update)
    {
        BikeDebugState snapshot;
        lock (_stateLock)
        {
            _state = update(_state);
            snapshot = _state;
        }
        StateChanged?.Invoke(snapshot);
    }

    private void PushState(string label)
    {
        UpdateState(s => s with
        {
            IsBusy = label is not ("Done" or "Idle" or "Disconnected"),
            ConnectionLabel = label,
            LastError = "",
        });
        PushLog(label);
    }

    private void PushError(string message)
    {
        UpdateState(s => s with { IsBusy = false, ConnectionLabel = "Failed", LastError = message });
        PushLog($"ERROR: {message}");
    }

    private void PushLog(string message)
    {
        var line = message.Trim();
        UpdateState(current => current with { Logs = current.Logs.Append(line).TakeLast(200).ToList() });
        _log.Debug(line);
    }
}
