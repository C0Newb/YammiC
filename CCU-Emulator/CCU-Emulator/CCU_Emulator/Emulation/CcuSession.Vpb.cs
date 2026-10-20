using CCU_Emulator.Protocol;

namespace CCU_Emulator.Emulation;

public sealed partial class CcuSession
{
    private readonly SppFrameReader _spp = new();
    private readonly VpbReceiver _vpb = new();

    private void OnSppConnection(bool up)
    {
        State.SppConnected = up;
        Log.Info("SPP", up ? "classic link up" : "classic link down");
        _spp.Reset();
        if (up)
        {
            SendSpp(SppFrame.Encode(CcuPackets.BleConnectInformation(Settings.CcuId, Settings.PassKey)), "BleConnectInformation");
        }
        else
        {
            ResetVpb();
        }
    }

    private async Task ConnectSppAsync()
    {
        try { await _transport.ConnectSppAsync(); }
        catch (Exception e) { _post(() => Log.Info("SPP", $"connect failed: {e.Message}")); }
    }

    private void SendSpp(byte[] framed, string name)
    {
        Log.Add(Direction.Tx, "Spp", name, framed);
        _ = SendSppAsync(framed);
    }

    private async Task SendSppAsync(byte[] framed)
    {
        try { await _transport.SendSppAsync(framed); }
        catch (Exception e) { _post(() => Log.Info("SPP", $"send failed: {e.Message}")); }
    }

    private void ResetVpb()
    {
        _vpb.Reset();
        State.VpbReceiving = false;
        State.VpbReceivedBytes = 0;
        State.VpbTotalBytes = 0;
    }

    private void HandleVpbRequest()
    {
        if (Settings.Behavior.RejectVpbRequests)
        {
            SendCcu(CcuPackets.VpbResponseNg(), "VpbResponseNg");
            return;
        }
        ResetVpb();
        State.VpbReceiving = true;
        SendCcu(CcuPackets.VpbGraphicRequest(), "VpbGraphicRequest");
    }

    private void HandleVpbChunk(VpbChunkMessage chunk)
    {
        var (response, image) = _vpb.Accept(chunk);
        SendCcu(CcuPackets.VpbChunkAck(chunk.ContentId, chunk.Index, response), $"VpbChunkAck ({response})");

        State.VpbReceiving = _vpb.InProgress;
        State.VpbReceivedBytes = _vpb.ReceivedBytes;
        State.VpbTotalBytes = _vpb.TotalSize;
        if (image is not null)
        {
            State.VpbImage = image;
            Log.Info("VPB", $"image complete ({image.Length} bytes)");
        }
    }

    private void HandleVpbClear()
    {
        ResetVpb();
        State.VpbImage = null;
        SendCcu(CcuPackets.VpbClearOk(), "VpbClearOk");
    }
}
