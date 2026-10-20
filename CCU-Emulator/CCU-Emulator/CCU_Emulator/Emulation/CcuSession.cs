using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;

namespace CCU_Emulator.Emulation;

/// <summary>
/// The emulated CCU: wires a transport to the protocol codecs, keeps <see cref="CcuState"/>, and
/// logs every packet. Every transport event is handed to <c>post</c> so all state is touched on
/// one thread (the UI thread in the app, inline in tests).
/// </summary>
public sealed partial class CcuSession
{
    private readonly ICcuTransport _transport;
    private readonly Action<Action> _post;
    private readonly DdtReceiver _ddt = new();

    public CcuSettings Settings { get; }
    public CcuState State { get; } = new();
    public PacketLog Log { get; } = new();

    public CcuSession(ICcuTransport transport, CcuSettings settings, Action<Action>? post = null)
    {
        _transport = transport;
        Settings = settings;
        _post = post ?? (a => a());

        _transport.DataReceived += (channel, data) => _post(() => OnData(channel, data));
        _transport.BleConnectionChanged += up => _post(() => OnBleConnection(up));
        _transport.SppConnectionChanged += up => _post(() => OnSppConnection(up));
        _transport.Diagnostic += message => _post(() => Log.Info("Transport", message));

        _ddt.StatusNotify += status => Send(CcuChannel.DdtStatus, status, "DdtStatus");
        _ddt.MessageComplete += message => HandleMessage(message, "DDT");
        _ddt.Diagnostic += message => Log.Info("DDT", message);
    }

    // ── Transport events ────────────────────────────────────────────────────────

    private void OnData(CcuChannel channel, byte[] value)
    {
        switch (channel)
        {
            case CcuChannel.SimpleWrite: HandleMessage(value, "SimpleWrite"); break;
            case CcuChannel.DdtControl: _ddt.OnControl(value); break;
            case CcuChannel.DdtData: _ddt.OnData(value); break;
            case CcuChannel.Spp:
                foreach (var frame in _spp.Feed(value)) HandleMessage(frame, "SPP");
                break;
            default: Log.Info("Transport", $"unexpected data on {channel}"); break;
        }
    }

    private void OnBleConnection(bool connected)
    {
        State.BleConnected = connected;
        Log.Info("BLE", connected ? "phone connected" : "phone disconnected");
        if (connected)
        {
            if (Settings.SppEnabled) _ = ConnectSppAsync();
        }
        else
        {
            ResetConnectionState();
        }
    }

    private void ResetConnectionState()
    {
        _ddt.Reset();
        State.Authenticated = false;
        State.AppId = 0;
        State.CallRinging = false;
        State.CallActive = false;
        ResetVpb();
        _spp.Reset();
    }

    // ── Sending ─────────────────────────────────────────────────────────────────

    private void Send(CcuChannel channel, byte[] data, string name)
    {
        Log.Add(Direction.Tx, channel.ToString(), name, data);
        _ = SendAsync(channel, data);
    }

    private async Task SendAsync(CcuChannel channel, byte[] data)
    {
        try { await _transport.NotifyAsync(channel, data); }
        catch (Exception e) { _post(() => Log.Info("Transport", $"notify {channel} failed: {e.Message}")); }
    }

    // ── Receiving ───────────────────────────────────────────────────────────────

    private void HandleMessage(byte[] data, string via)
    {
        PhoneMessage message;
        try
        {
            message = PhoneMessageDecoder.Decode(data);
        }
        catch (MalformedPacketException e)
        {
            Log.Add(Direction.Rx, via, "Malformed", data, e.Message);
            return;
        }

        Log.Add(Direction.Rx, via, message.GetType().Name.Replace("Message", ""), data, message.ToString() ?? "");
        Apply(message);
    }

    private void Apply(PhoneMessage message)
    {
        switch (message)
        {
            case AuthenticationMessage a:
                HandleAuth(a);
                break;

            case IdentifyAppMessage i:
                State.AppId = i.AppId;
                break;

            case MusicMetaDataMessage m:
                State.MusicArtist = m.Artist;
                State.MusicAlbum = m.Album;
                State.MusicSong = m.Song;
                State.MusicDurationSeconds = m.DurationSeconds;
                State.MusicElapsedSeconds = m.ElapsedSeconds;
                State.MusicPlayback = m.State;
                State.MusicRate = m.Rate;
                break;

            case WeatherLocationMessage w:
                State.WeatherLocation = w.Name;
                break;

            case WeatherInfoMessage w:
                State.UpsertWeather(new WeatherEntry(w.Slot, w.Type, w.Temperature, w.MaxTemperature, w.MinTemperature, w.PrecipitationPercent));
                break;

            case DeviceTimeMessage t:
                State.PhoneTime = t.Time;
                _deviceTimeAnswered = true;
                break;

            case NotificationMessage n:
                State.AddNotification(new NotificationEntry(n.Id, n.Category, n.Timestamp, n.Title, n.SubTitle, n.Detail));
                break;

            case NotificationLifecycleMessage { Operation: CommandCode.RemoveNotification } l:
                State.RemoveNotification(l.Id);
                break;

            case NotificationCategoryMessage c:
                State.NotificationCategories = c.Enabled;
                break;

            case IncomingCallInfoMessage c when c.Missed:
                State.MissedCalls++;
                State.CallRinging = false;
                break;

            case IncomingCallInfoMessage c when State.CallRinging && c.Name == State.CallerName && c.Number == State.CallerNumber:
                // YammiC ends a ring (rejected, answered elsewhere, hung up) by re-sending the same info with the missed flag clear.
                State.CallRinging = false;
                break;

            case IncomingCallInfoMessage c:
                State.CallerName = c.Name;
                State.CallerNumber = c.Number;
                State.CallRinging = true;
                break;

            case CallChangeMessage c:
                State.CallActive = c.Started;
                State.CallRinging = false;   // answered, or the call is over
                break;

            case PhoneStateMessage p:
                State.Battery = p.Battery;
                State.Charging = p.Charging;
                State.PhoneVolume = p.PhoneVolume;
                State.MediaVolume = p.MediaVolume;
                State.Signal = p.Signal;
                State.Thermal = p.Thermal;
                State.HeadsetConnected = p.HeadsetConnected;
                State.OnHook = p.OnHook;
                break;

            case ThermalStateMessage t:
                State.Thermal = t.State;
                break;

            case CellSignalResponseMessage s:
                State.Signal = s.Signal;
                break;

            case HeadsetStateResponseMessage h:
                State.HeadsetConnected = h.Connected;
                break;

            case VolumeLevelMessage { Type: VolumeType.Phone } v:
                State.PhoneVolume = v.Percent;
                break;

            case VolumeLevelMessage v:
                State.MediaVolume = v.Percent;
                break;

            case VpbRequestMessage:
                HandleVpbRequest();
                break;

            case VpbChunkMessage c:
                HandleVpbChunk(c);
                break;

            case VpbClearMessage:
                HandleVpbClear();
                break;

            // Logged by HandleMessage; nothing to track.
            case VpbGraphicResponseNgMessage:
            case LanguageResponseMessage:
            case VolumeControllableResponseMessage:
            case PongMessage:
            case UnknownMessage:
                break;
        }
    }

    private void HandleAuth(AuthenticationMessage a)
    {
        var ok = !Settings.Behavior.ForceAuthFailure && a.CcuId == Settings.CcuId && a.PassKey == Settings.PassKey;
        State.Authenticated = ok;
        Send(CcuChannel.MainRx, CcuPackets.AuthenticationResponse(ok), ok ? "AuthenticationResponse (ok)" : "AuthenticationResponse (failed)");
        Log.Info("Auth", ok ? "phone authenticated" : "rejected: wrong CCUID/passkey or forced failure");
        if (ok) StartBootstrap();
    }
}
