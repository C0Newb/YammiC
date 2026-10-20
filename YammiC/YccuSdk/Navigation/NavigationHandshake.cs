using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Navigation.Protocol;

namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// NaviLite connection handshake orchestrator.
///
/// Implements the 4-step handshake sequence required to authenticate with the Yamaha
/// CCU (Connected Control Unit) over the NaviLite protocol:
///
///   1. ESN_UPDATE -> ESN_ACK
///      The CCU announces its ESN (serial number); we acknowledge.
///
///   2. AUTH_REQUEST
///      Phone initiates authentication.
///
///   3. SEC_DATA -> SEC_DATA_ACK
///      CCU sends obfuscated authentication data (part number + nonce XOR 0x0a);
///      phone de-obfuscates, extracts the 4-byte nonce, and echoes it back.
///
///   4. Setup burst
///      Phone sends a series of initial status frames to populate the CCU's display
///      (NAV_STATUS, DAY_NIGHT, HOME, OFFICE, GPS, etc.).
///
/// Ported from Pillion's Handshake.perform(): while awaiting ESN_UPDATE/SEC_DATA, unrelated
/// frames the CCU sends in between (e.g. dash button-press requests) are skipped rather than
/// treated as a protocol error. No I/O timeouts are imposed; blocking reads on real channels
/// are expected.
/// </summary>
public class NavigationHandshake
{
    // appVersion 1820 (0x071c) + extra, matching the reference SDK's AUTH_REQUEST payload.
    private static readonly byte[] AuthRequestPayload = { 0x1c, 0x07, 0, 1, 0, 0, 0, 0 };

    // (serviceType, payloadDataType, payload) - the post-auth setup the CCU expects before
    // it renders images, ported from the reference SDK's setup burst.
    private static readonly (int ServiceType, int PayloadDataType, byte[] Payload)[] SetupBurst =
    {
        (NaviLiteServiceType.NavStatus, NaviLiteConstants.PdtValue, new byte[] { 0, 0 }),
        (NaviLiteServiceType.DayNight, NaviLiteConstants.PdtValue, new byte[] { 1, 0 }),
        // HOME / OFFICE go between DAY_NIGHT and GPS when requested - see SendSetupBurst.
        (NaviLiteServiceType.Gps, NaviLiteConstants.PdtValue, new byte[] { 1, 0 }),
        (NaviLiteServiceType.AppSetting, NaviLiteConstants.PdtValue, new byte[] { 0, 0 }),
        (
            NaviLiteServiceType.Zoom,
            NaviLiteConstants.PdtPointer,
            new byte[] { 0x07, 0x19, 0x06, 0x00, 0x30, 0x2e, 0x32, 0x20, 0x6d, 0x69 }),
        (NaviLiteServiceType.Road, NaviLiteConstants.PdtPointer, Array.Empty<byte>()),
        (
            NaviLiteServiceType.SpeedLimit,
            NaviLiteConstants.PdtPointer,
            new byte[] { 0, 0, 0, 0, 0x03, 0x6d, 0x70, 0x68 }),
        (NaviLiteServiceType.Gps, NaviLiteConstants.PdtValue, new byte[] { 1, 0 }),
        (NaviLiteServiceType.AppSetting, NaviLiteConstants.PdtValue, new byte[] { 1, 0 }),
    };

    /// <summary>
    /// The status frames StreetCross re-sends each time the rider opens the navigation screen (NAV_STATUS, GPS, APP_SETTING and
    /// the zoom level), plus HOME/OFFICE when reported - so the CCU's view of them is refreshed rather than only set once at
    /// connect.
    /// </summary>
    public static IReadOnlyList<(int ServiceType, int PayloadDataType, byte[] Payload)> StatusRefresh(bool reportHomeAndOffice, float zoom = 1f)
    {
        var frames = new List<(int, int, byte[])>
        {
            (NaviLiteServiceType.NavStatus, NaviLiteConstants.PdtValue, new byte[] { 0, 0 }),
        };
        if (reportHomeAndOffice)
        {
            frames.Add((NaviLiteServiceType.Home, NaviLiteConstants.PdtValue, new byte[] { 1 }));
            frames.Add((NaviLiteServiceType.Office, NaviLiteConstants.PdtValue, new byte[] { 1 }));
        }
        frames.Add((NaviLiteServiceType.Gps, NaviLiteConstants.PdtValue, new byte[] { 1, 0 }));
        frames.Add((NaviLiteServiceType.AppSetting, NaviLiteConstants.PdtValue, new byte[] { 1, 0 }));
        frames.Add((
            NaviLiteServiceType.Zoom,
            NaviLiteConstants.PdtPointer,
            NaviLiteZoom.Payload(zoom)));
        return frames;
    }

    private readonly INavByteChannel _channel;
    private readonly Logger _log;
    private readonly bool _reportHomeAndOffice;
    private readonly NavDayNightMode _dayNightMode;
    private readonly float _zoom;

    /// <param name="reportHomeAndOffice">
    /// Tell the CCU a home and an office are saved (HOME/OFFICE = 0x01), which is what makes its "Go home"/"Go to work"
    /// buttons send GO_HOME_REQUEST/GO_OFFICE_REQUEST. Each payload is a single `isSet & 1` byte (confirmed from
    /// StreetCross's native packer). When false the CCU shows both as "not set" without ever asking the phone.
    /// </param>
    /// <param name="dayNightMode">Initial DAY_NIGHT mode in the setup burst.</param>
    public NavigationHandshake(
        INavByteChannel channel,
        Logger? logger = null,
        bool reportHomeAndOffice = false,
        NavDayNightMode dayNightMode = NavDayNightMode.Day,
        float zoom = 1f)
    {
        _channel = channel;
        _reportHomeAndOffice = reportHomeAndOffice;
        _dayNightMode = dayNightMode;
        _zoom = zoom;
        _log = (logger ?? new Logger("NavigationHandshake", Array.Empty<ILogSink>())).Section("NavigationHandshake");
        Reader = new NavFrameReader(channel);
    }

    /// <summary>
    /// Exposed so callers can keep reading off the same buffered stream after the handshake
    /// completes (e.g. to drain IMAGE_ACK frames) without losing bytes already read into it.
    /// </summary>
    public NavFrameReader Reader { get; }

    /// <summary>The CCU part number announced in SEC_DATA, or null before the handshake got that far / if unreadable.</summary>
    public string? PartNumber { get; private set; }

    /// <summary>
    /// Execute the full handshake sequence.
    /// Blocks on channel I/O until handshake completes or fails.
    /// </summary>
    public void Execute()
    {
        _log.Debug("waiting for ESN_UPDATE");
        // Step 1: Receive ESN_UPDATE and respond with ESN_ACK
        HandleEsnUpdate();
        SendEsnAck();

        // Step 2: Send AUTH_REQUEST
        SendAuthRequest();

        _log.Debug("waiting for SEC_DATA");
        // Step 3: Receive SEC_DATA and respond with SEC_DATA_ACK
        ReceiveSecDataAndRespond();

        // Step 4: Send setup burst
        SendSetupBurst();
        _log.Debug("setup burst sent, authenticated");
    }

    /// <summary>
    /// Waits for ESN_UPDATE, skipping any unrelated frames the CCU sends first.
    /// Returns true once received; throws only if the channel closes before it arrives.
    /// </summary>
    public bool HandleEsnUpdate()
    {
        var frame = AwaitService(NaviLiteServiceType.EsnUpdate);
        _log.Debug($"ESN_UPDATE received (payload {frame.Payload.Length}B)");
        return true;
    }

    /// <summary>
    /// Send ESN_ACK to the CCU. Payload is a fixed 2-byte value (<c>01 00</c>), matching the
    /// reference SDK - an empty payload is silently ignored by the CCU.
    /// </summary>
    public void SendEsnAck()
    {
        var frame = NaviLiteCodec.Build(
            frameType: NaviLiteConstants.FrameTypePhone,
            serviceType: NaviLiteServiceType.EsnAck,
            payloadDataType: NaviLiteConstants.PdtValue,
            payload: new byte[] { 1, 0 });
        _channel.Write(frame);
        _log.Debug("sent ESN_ACK");
    }

    /// <summary>
    /// Send AUTH_REQUEST to initiate authentication with the CCU. Payload encodes the app
    /// version (1820 / 0x071c) the reference SDK sends; the CCU does not proceed to SEC_DATA
    /// for a request with an empty/malformed payload.
    /// </summary>
    public void SendAuthRequest()
    {
        var frame = NaviLiteCodec.Build(
            frameType: NaviLiteConstants.FrameTypePhone,
            serviceType: NaviLiteServiceType.AuthRequest,
            payloadDataType: NaviLiteConstants.PdtPointer,
            payload: AuthRequestPayload);
        _channel.Write(frame);
        _log.Debug("sent AUTH_REQUEST");
    }

    /// <summary>
    /// Receive SEC_DATA from the CCU (skipping any unrelated frames first) and respond with
    /// SEC_DATA_ACK.
    ///
    /// The SEC_DATA payload contains the CCU's part number and a 4-byte nonce,
    /// all XOR-ed with 0x0a for obfuscation. We de-obfuscate, extract the nonce,
    /// and send it back in the SEC_DATA_ACK frame.
    /// </summary>
    public void ReceiveSecDataAndRespond()
    {
        var frame = AwaitService(NaviLiteServiceType.SecData);
        // The part number also tells us the CCU model (and therefore its display resolution -
        // see Pillion's "Vehicle Model Detection"), useful for diagnosing model-specific issues.
        string partNumber;
        try
        {
            partNumber = NaviLiteAuth.PartNumber(frame.Payload);
            PartNumber = partNumber;
        }
        catch (Exception)
        {
            partNumber = "?";
            PartNumber = null;
        }
        _log.Debug($"SEC_DATA received: partNumber={partNumber}");

        // Extract the nonce and send it back
        var nonce = NaviLiteAuth.SecDataAckPayload(frame.Payload);
        var ackFrame = NaviLiteCodec.Build(
            frameType: NaviLiteConstants.FrameTypePhone,
            serviceType: NaviLiteServiceType.SecDataAck,
            payloadDataType: NaviLiteConstants.PdtPointer,
            payload: nonce);
        _channel.Write(ackFrame);
        _log.Debug("sent SEC_DATA_ACK");
    }

    /// <summary>
    /// Send the post-authentication setup burst: the values the reference SDK sends before
    /// the CCU accepts IMAGE frames. GPS and APP_SETTING are sent twice (once with initial
    /// values, again with final ones), matching the reference SDK exactly.
    /// </summary>
    public void SendSetupBurst()
    {
        var burst = SetupBurst.ToList();
        burst[1] = (NaviLiteServiceType.DayNight, NaviLiteConstants.PdtValue, new byte[] { (byte)_dayNightMode, 0 });
        var zoomIndex = burst.FindIndex(f => f.ServiceType == NaviLiteServiceType.Zoom);
        burst[zoomIndex] = (NaviLiteServiceType.Zoom, NaviLiteConstants.PdtPointer, NaviLiteZoom.Payload(_zoom));
        if (_reportHomeAndOffice)
        {
            // Order observed from StreetCross: NAV_STATUS, DAY_NIGHT, HOME, OFFICE, GPS, ...
            burst.InsertRange(2, new (int ServiceType, int PayloadDataType, byte[] Payload)[]
            {
                (NaviLiteServiceType.Home, NaviLiteConstants.PdtValue, new byte[] { 1 }),
                (NaviLiteServiceType.Office, NaviLiteConstants.PdtValue, new byte[] { 1 }),
            });
        }

        foreach (var (serviceType, payloadDataType, payload) in burst)
        {
            var frame = NaviLiteCodec.Build(
                frameType: NaviLiteConstants.FrameTypePhone,
                serviceType: serviceType,
                payloadDataType: payloadDataType,
                payload: payload);
            _channel.Write(frame);
            _log.Debug($"setup burst: sent serviceType={serviceType} payload={string.Join(" ", payload.Select(b => b.ToString("x2")))}");
        }
    }

    /// <summary>Reads frames until one matches <paramref name="serviceType"/>, skipping any that don't.</summary>
    private NavFrameView AwaitService(int serviceType)
    {
        var frame = Reader.Next();
        while (frame.ServiceType != serviceType)
        {
            _log.Debug($"awaitService({serviceType}): skipping frame serviceType={frame.ServiceType} ({frame.Payload.Length}B)");
            frame = Reader.Next();
        }
        return frame;
    }
}
