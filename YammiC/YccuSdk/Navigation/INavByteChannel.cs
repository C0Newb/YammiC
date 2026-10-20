namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// A bidirectional raw byte link to the bike device. The frame reader and all higher-level
/// protocol handlers depend on this abstraction; platforms provide concrete implementations
/// (e.g., Bluetooth RFCOMM).
///
/// Ported from Pillion's ByteChannel.
/// </summary>
public interface INavByteChannel
{
    void Open();

    void Write(byte[] bytes);

    /// <summary>
    /// Blocking read of up to <paramref name="buffer"/>.Length bytes.
    /// Returns the count of bytes read (&gt;0), or -1 when the channel is closed.
    /// </summary>
    int Read(byte[] buffer);

    void Close();
}
