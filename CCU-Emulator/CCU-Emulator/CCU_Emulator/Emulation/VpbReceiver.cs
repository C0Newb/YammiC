using CCU_Emulator.Protocol;

namespace CCU_Emulator.Emulation;

/// <summary>Reassembles a Virtual Pit Board image from CRC-checked, in-order chunks.</summary>
public sealed class VpbReceiver
{
    private MemoryStream? _image;
    private int _contentId;
    private int _expectedIndex;
    private int _totalSize;

    public bool InProgress => _image != null;
    public int ReceivedBytes => (int)(_image?.Length ?? 0);
    public int TotalSize => _totalSize;

    public void Reset()
    {
        _image = null;
        _expectedIndex = 0;
        _totalSize = 0;
    }

    /// <summary>Returns the ack to send and, when the last chunk completes a valid image, the image bytes.</summary>
    public (ChunkResponse Response, byte[]? Image) Accept(VpbChunkMessage chunk)
    {
        if (!chunk.CrcOk) return (ChunkResponse.Retry, null);

        if (_image is null || chunk.ContentId != _contentId)
        {
            if (chunk.Index != 0) { Reset(); return (ChunkResponse.Abort, null); }
            _image = new MemoryStream();
            _contentId = chunk.ContentId;
            _expectedIndex = 0;
            _totalSize = chunk.TotalSize;
        }

        if (chunk.TotalSize != _totalSize) { Reset(); return (ChunkResponse.Abort, null); }
        if (chunk.Index == _expectedIndex - 1) return (ChunkResponse.Ok, null);   // resend of the last chunk
        if (chunk.Index != _expectedIndex) { Reset(); return (ChunkResponse.Abort, null); }
        if (_image.Length + chunk.Data.Length > _totalSize) { Reset(); return (ChunkResponse.Abort, null); }

        _image.Write(chunk.Data);
        _expectedIndex++;

        if (chunk.Index != chunk.TotalIndex) return (ChunkResponse.Ok, null);

        var complete = _image.Length == _totalSize;
        var image = _image.ToArray();
        Reset();
        return complete ? (ChunkResponse.Ok, image) : (ChunkResponse.Abort, null);
    }
}
