namespace CCU_Emulator.Protocol;

/// <summary>
/// CCU side of the phone→CCU DDT session. The phone writes <c>[reqId][size 3B]</c> to the control
/// characteristic, the CCU notifies status <c>[1][reqId]</c>, the phone writes
/// <c>[reqId][offset 3B][data]</c> chunks to the data characteristic, and when the declared size
/// is filled the CCU notifies status <c>[0][0]</c>.
/// </summary>
public sealed class DdtReceiver
{
    public const int MaxSize = 1_000_000;

    public event Action<byte[]>? StatusNotify;
    public event Action<byte[]>? MessageComplete;
    public event Action<string>? Diagnostic;

    private byte _reqId;
    private byte[]? _buf;
    private bool[]? _filled;
    private int _filledCount;

    public bool Active => _buf != null;

    /// <summary>Value of the (readable) status characteristic: idle <c>00 00</c>, active <c>01 reqId</c>.</summary>
    public byte[] StatusValue => Active ? [1, _reqId] : [0, 0];

    public void OnControl(byte[] value)
    {
        if (value.Length < 4) { Diagnostic?.Invoke($"DDT control write too short ({value.Length} bytes)"); return; }
        var size = (value[1] << 16) | (value[2] << 8) | value[3];
        if (size <= 0 || size > MaxSize) { Diagnostic?.Invoke($"DDT control write with bad size {size}"); return; }
        if (Active) Diagnostic?.Invoke("DDT session started while another was active; dropping the old one");

        _reqId = value[0];
        _buf = new byte[size];
        _filled = new bool[size];
        _filledCount = 0;
        StatusNotify?.Invoke([1, _reqId]);
    }

    public void OnData(byte[] value)
    {
        if (_buf is null || _filled is null) { Diagnostic?.Invoke("DDT data with no active session"); return; }
        if (value.Length < 4 || value[0] != _reqId) { Diagnostic?.Invoke("DDT data with wrong request id or too short"); return; }

        var offset = (value[1] << 16) | (value[2] << 8) | value[3];
        var n = value.Length - 4;
        if (offset + n > _buf.Length) { Diagnostic?.Invoke("DDT chunk exceeds the declared size"); return; }

        for (var i = 0; i < n; i++)
        {
            _buf[offset + i] = value[4 + i];
            if (!_filled[offset + i]) { _filled[offset + i] = true; _filledCount++; }
        }

        if (_filledCount == _buf.Length)
        {
            var message = _buf;
            Reset();
            MessageComplete?.Invoke(message);   // a real CCU answers (e.g. auth) before the completion status
            StatusNotify?.Invoke([0, 0]);
        }
    }

    public void Reset()
    {
        _buf = null;
        _filled = null;
        _filledCount = 0;
    }
}
