using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets.App;

namespace CNCO.YccuSdk.Packets;

public class YmcBasePacketHandler : IYmcPacketHandler
{
    private static readonly TimeSpan DdtTimeout = TimeSpan.FromSeconds(5);

    private readonly Logger _log;
    private int _requestIdCounter = 1;
    private readonly SemaphoreSlim _ddtMutex = new(1, 1);

    public YmcBasePacketHandler(Logger logger)
    {
        _log = logger.Section("PacketHandler");
    }

    public async Task SendAsync(YmcConnectionContext connectionContext, AppOutgoingPacket request)
    {
        // Keeps the GATT write callbacks of noisy commands out of the UI log (see SuppressUiLogAttribute).
        using var uiLog = UiLogFilter.BeginSend(request.CommandCode);
        await (request.PacketTransport switch
        {
            AppOutgoingPacket.Transport.Simple => SendSimpleCommandAsync(connectionContext, request),
            AppOutgoingPacket.Transport.Ddt => SendDdtCommandAsync(connectionContext, request),
            AppOutgoingPacket.Transport.Spp => SendSppCommandAsync(connectionContext, request),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a command over the Classic-Bluetooth SPP socket instead of BLE GATT.
    /// Used only for <see cref="YmcCommandCode.SendVirtualPitBoardGraphic"/> -
    /// see <see cref="AppOutgoingPacket.Transport.Spp"/>.
    /// </summary>
    private async Task SendSppCommandAsync(YmcConnectionContext connectionContext, AppOutgoingPacket request)
    {
        var packet = request.Serialize(NextRequestId());
        var label = $"{request.GetType().FullName} (#{request.CommandCode})";
        var framed = YmcBikeProtocol.SppFramePacket(packet);
        _log.Debug($"sendSppCommand[{label}]: {packet.Length} bytes framed as {framed.Length} bytes: {framed.ToHexPreview()}");
        if (!await connectionContext.WriteSppAsync(framed).ConfigureAwait(false))
        {
            _log.Debug($"sendSppCommand[{label}]: no live SPP socket, dropped");
        }
    }

    private byte NextRequestId()
    {
        var id = _requestIdCounter;
        _requestIdCounter = _requestIdCounter >= 255 ? 1 : _requestIdCounter + 1;
        return (byte)id;
    }

    /// <summary>
    /// Sends a single-packet command directly to <see cref="YmcBikeProtocol.SimpleWriteUuid"/>.
    /// Used for: IdentifyAppRequest, WeatherInfo, GetDeviceLanguageResponse, AliveCheckResponse.
    /// No DDT handshake or fragmentation.
    /// </summary>
    private async Task SendSimpleCommandAsync(YmcConnectionContext connectionContext, AppOutgoingPacket request)
    {
        var packet = request.Serialize(NextRequestId());
        var label = $"{request.GetType().FullName} (#{request.CommandCode})";

        Guid characteristic;
        if (connectionContext.HasCharacteristic(YmcBikeProtocol.SimpleWriteUuid))
        {
            characteristic = YmcBikeProtocol.SimpleWriteUuid;
        }
        else if (connectionContext.HasCharacteristic(YmcBikeProtocol.DataUuid)) // fallback
        {
            characteristic = YmcBikeProtocol.DataUuid;
        }
        else
        {
            throw new InvalidOperationException($"No write characteristic available for {label}");
        }
        _log.Debug($"sendSimpleCommand[{label}]: {packet.Length} bytes to {characteristic}, body:\n{packet.ToHexPreview()}");
        await connectionContext.WriteAsync(characteristic, packet).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a command via the DDT session protocol:
    /// 1. Write size-announcement to <see cref="YmcBikeProtocol.ControlUuid"/>.
    /// 2. Await active-status notification from <see cref="YmcBikeProtocol.StatusUuid"/>.
    /// 3. Write data chunks to <see cref="YmcBikeProtocol.DataUuid"/>.
    /// 4. Await completion-status notification.
    ///
    /// Only one DDT session can be in flight at a time (there's a single shared session slot
    /// on the CCU). The reference SDK (`d.java`) rejects a new DDT send outright with
    /// `McError.DdtSending` when one is already active - it does not queue. We match that:
    /// if the channel is busy, drop this send instead of blocking and piling up a backlog of
    /// increasingly stale sends, which previously starved other DDT traffic and could time out
    /// the connection.
    /// </summary>
    private async Task SendDdtCommandAsync(YmcConnectionContext connectionContext, AppOutgoingPacket request)
    {
        await _ddtMutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!connectionContext.HasCharacteristic(YmcBikeProtocol.ControlUuid))
                throw new InvalidOperationException("Control characteristic not found");
            if (!connectionContext.HasCharacteristic(YmcBikeProtocol.DataUuid))
                throw new InvalidOperationException("Data characteristic not found");

            var requestId = NextRequestId();
            var body = request.Serialize(requestId);

            _log.Debug(
                $"sendDdtRequest: cmd={(body[0] << 8) | body[1]}, reqId={requestId}, framedSize={body.Length}, mtu={connectionContext.NegotiatedMtu}, body:\n{body.ToHexPreview()}");

            // CheckInactive: verify the DDT channel is idle before starting a session.
            // The SDK (j.java) reads status before writing control - if the channel is busy
            // from a previous incomplete session, the CCU will silently ignore the control write.
            if (connectionContext.HasCharacteristic(YmcBikeProtocol.StatusUuid))
            {
                var statusValue = await connectionContext.ReadAsync(YmcBikeProtocol.StatusUuid).ConfigureAwait(false);
                if (statusValue is { Length: > 0 } && statusValue[0] != 0)
                {
                    _log.Debug($"sendDdtRequest: channel busy status={statusValue.ToHexString()}, aborting");
                    return;
                }
            }

            // Subscribe to BOTH status notifications BEFORE doing any writes.
            // The CCU's active and completion signals can arrive almost simultaneously with write
            // acknowledgements; subscribing first prevents the race where a notification is emitted
            // before our listeners start.
            using var cancellation = new CancellationTokenSource();
            var activeTask = connectionContext.Notifications.FirstAsync(
                e => e.Uuid == YmcBikeProtocol.StatusUuid && e.Value.Length >= 2 && e.Value[0] == 1 && e.Value[1] == requestId,
                cancellation.Token);
            var completionTask = connectionContext.Notifications.FirstAsync(
                e => e.Uuid == YmcBikeProtocol.StatusUuid && e.Value.Length >= 2 && e.Value[0] == 0 && e.Value[1] == 0,
                cancellation.Token);

            try
            {
                var sessionStartPacket = YmcBikeProtocol.SessionStartPacket(requestId, body.Length);
                _log.Debug($"sendDdtRequest: sessionStart:\n{sessionStartPacket.ToHexString()}");
                await connectionContext.WriteAsync(YmcBikeProtocol.ControlUuid, sessionStartPacket).ConfigureAwait(false);

                var (activeSeen, _) = await activeTask.WithTimeoutAsync(DdtTimeout).ConfigureAwait(false);
                if (!activeSeen)
                {
                    _log.Debug($"sendDdtRequest: no active-status notification for reqId={requestId}");
                }

                var chunkSize = YmcBikeProtocol.PayloadChunkSize(connectionContext.NegotiatedMtu);
                var offset = 0;
                while (offset < body.Length)
                {
                    var end = Math.Min(offset + chunkSize, body.Length);
                    var chunk = body[offset..end];
                    var chunkPacket = YmcBikeProtocol.ChunkPacket(requestId, offset, chunk);
                    _log.Debug($"sendDdtRequest: chunk offset={offset}..{end}:\n{chunkPacket.ToHexPreview()}");
                    await connectionContext.WriteAsync(YmcBikeProtocol.DataUuid, chunkPacket).ConfigureAwait(false);
                    offset = end;
                }

                var (completed, _) = await completionTask.WithTimeoutAsync(DdtTimeout).ConfigureAwait(false);
                if (!completed)
                {
                    _log.Debug($"sendDdtRequest: no completion-status notification for reqId={requestId}");
                }
            }
            finally
            {
                // Stop any still-pending listeners (and swallow their cancellation) so nothing leaks.
                cancellation.Cancel();
                await Task.WhenAll(activeTask, completionTask).ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
            }
        }
        finally
        {
            _ddtMutex.Release();
        }
    }
}
