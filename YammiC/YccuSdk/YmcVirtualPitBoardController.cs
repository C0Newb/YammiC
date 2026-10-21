using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk;

/// <summary>
/// Drives the Virtual Pit Board handshake: request, then (graphic request | reject), then chunked graphic
/// send with per-chunk retry, and the separate clear flow. Mirrors the reference SDK's
/// `MpnSettingManager` state machine for `McMpnType.VIRTUAL_PITBOARD` (PhotoFrame/PushNotification
/// share the same machine upstream but aren't implemented here).
///
/// Only one send (or clear) can be in flight at a time - matches the CCU's single MPN state slot.
/// </summary>
public class YmcVirtualPitBoardController
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    private readonly Logger _log;
    private readonly SemaphoreSlim _sendMutex = new(1, 1);

    private TaskCompletionSource<bool>? _graphicRequestDeferred;
    private TaskCompletionSource<CcuSendVirtualPitBoardGraphicResponse.ResponseType>? _chunkAckDeferred;
    private TaskCompletionSource<bool>? _clearDeferred;

    private int _nextContentId = 20101;

    public YmcVirtualPitBoardController(Logger logger)
    {
        _log = logger.Section("VirtualPitBoard");
    }

    /// <summary>
    /// Registers the CCU-side handlers this controller needs. Call once per connection.
    /// </summary>
    public void RegisterHandlers(YmcMessageHandler messageHandler)
    {
        messageHandler.Register(
            YmcCommandCode.VirtualPitBoardResponseNg,
            (ph, data) => new CcuVirtualPitBoardResponseNg(ph, data),
            OnVirtualPitBoardResponseNg);
        messageHandler.Register(
            YmcCommandCode.VirtualPitBoardGraphicRequest,
            (ph, data) => new CcuVirtualPitBoardGraphicRequest(ph, data),
            OnVirtualPitBoardGraphicRequest);
        messageHandler.Register(
            YmcCommandCode.SendVirtualPitBoardGraphicResponse,
            (ph, data) => new CcuSendVirtualPitBoardGraphicResponse(ph, data),
            OnSendVirtualPitBoardGraphicResponse);
        messageHandler.Register(
            YmcCommandCode.ClearVirtualPitBoardResponseOk,
            (ph, data) => new CcuClearVirtualPitBoardResponseOk(ph, data),
            OnClearVirtualPitBoardResponseOk);
        messageHandler.Register(
            YmcCommandCode.ClearVirtualPitBoardResponseNg,
            (ph, data) => new CcuClearVirtualPitBoardResponseNg(ph, data),
            OnClearVirtualPitBoardResponseNg);
    }

    private void OnVirtualPitBoardResponseNg(CcuVirtualPitBoardResponseNg packet) => _graphicRequestDeferred?.TrySetResult(false);

    private void OnVirtualPitBoardGraphicRequest(CcuVirtualPitBoardGraphicRequest packet) => _graphicRequestDeferred?.TrySetResult(true);

    private void OnSendVirtualPitBoardGraphicResponse(CcuSendVirtualPitBoardGraphicResponse packet) =>
        _chunkAckDeferred?.TrySetResult(packet.Response);

    private void OnClearVirtualPitBoardResponseOk(CcuClearVirtualPitBoardResponseOk packet) => _clearDeferred?.TrySetResult(true);

    private void OnClearVirtualPitBoardResponseNg(CcuClearVirtualPitBoardResponseNg packet) => _clearDeferred?.TrySetResult(false);

    /// <summary>
    /// Sends <paramref name="image"/> (already-encoded graphic bytes, e.g. JPEG) to the Virtual Pit Board.
    /// Returns true once every chunk was acknowledged, false if the CCU rejected the request or a
    /// chunk failed after retries. At most 256 chunks are supported (single-byte chunk index),
    /// i.e. images up to <see cref="AppSendVirtualPitBoardGraphic.ChunkSize"/> * 256 bytes.
    /// </summary>
    public async Task<bool> SendMessageAsync(
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext,
        byte[] image)
    {
        await _sendMutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (image.Length == 0)
            {
                _log.Debug("sendMessage: empty image, nothing to send");
                return false;
            }

            var contentId = _nextContentId;
            _nextContentId = _nextContentId >= int.MaxValue - 1 ? 1 : _nextContentId + 1;

            var graphicRequest = NewDeferred<bool>();
            _graphicRequestDeferred = graphicRequest;
            await new AppVirtualPitBoardRequest(packetHandler).SendAsync(connectionContext).ConfigureAwait(false);
            var (_, proceed) = await graphicRequest.Task.WithTimeoutAsync(ResponseTimeout).ConfigureAwait(false);
            _graphicRequestDeferred = null;
            if (!proceed)
            {
                _log.Debug("sendMessage: CCU rejected VirtualPitBoardRequest or timed out");
                return false;
            }

            const int chunkSize = AppSendVirtualPitBoardGraphic.ChunkSize;
            var chunkCount = (image.Length + chunkSize - 1) / chunkSize;
            if (chunkCount > 256)
            {
                _log.Debug($"sendMessage: image too large ({chunkCount} chunks > 256), aborting");
                await new AppVirtualPitBoardGraphicResponseNg(packetHandler).SendAsync(connectionContext).ConfigureAwait(false);
                return false;
            }
            var totalIndex = (byte)(chunkCount - 1);

            for (var index = 0; index < chunkCount; index++)
            {
                var start = index * chunkSize;
                var end = Math.Min(start + chunkSize, image.Length);
                var chunk = image[start..end];

                var attempt = 0;
                var success = false;
                while (attempt < 3 && !success)
                {
                    var chunkAck = NewDeferred<CcuSendVirtualPitBoardGraphicResponse.ResponseType>();
                    _chunkAckDeferred = chunkAck;
                    await new AppSendVirtualPitBoardGraphic(
                        packetHandler,
                        contentId,
                        (byte)index,
                        totalIndex,
                        image.Length,
                        chunk).SendAsync(connectionContext).ConfigureAwait(false);
                    var (acked, ackResponse) = await chunkAck.Task.WithTimeoutAsync(ResponseTimeout).ConfigureAwait(false);
                    var response = acked ? ackResponse : CcuSendVirtualPitBoardGraphicResponse.ResponseType.FailureNotRetry;
                    _chunkAckDeferred = null;
                    switch (response)
                    {
                        case CcuSendVirtualPitBoardGraphicResponse.ResponseType.Success:
                            success = true;
                            break;

                        case CcuSendVirtualPitBoardGraphicResponse.ResponseType.FailureRetry:
                            attempt++;
                            break;

                        default:
                            _log.Debug($"sendMessage: chunk {index} aborted (response={response})");
                            return false;
                    }
                }
                if (!success)
                {
                    _log.Debug($"sendMessage: chunk {index} failed after retries");
                    return false;
                }
            }
            return true;
        }
        finally
        {
            _sendMutex.Release();
        }
    }

    /// <summary>
    /// Clears the Virtual Pit Board display. Returns true on `ClearVirtualPitBoardResponseOk`.
    /// </summary>
    public async Task<bool> ClearAsync(IYmcPacketHandler packetHandler, YmcConnectionContext connectionContext)
    {
        var clear = NewDeferred<bool>();
        _clearDeferred = clear;
        await new AppClearVirtualPitBoard(packetHandler).SendAsync(connectionContext).ConfigureAwait(false);
        var (_, result) = await clear.Task.WithTimeoutAsync(ResponseTimeout).ConfigureAwait(false);
        _clearDeferred = null;
        return result;
    }

    private static TaskCompletionSource<T> NewDeferred<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
