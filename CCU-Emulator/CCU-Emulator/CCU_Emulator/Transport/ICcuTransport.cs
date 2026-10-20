using CCU_Emulator.Emulation;

namespace CCU_Emulator.Transport;

/// <summary>
/// Bytes in, bytes out between the emulated CCU and a phone. Events may fire on any thread.
/// </summary>
public interface ICcuTransport : IAsyncDisposable
{
    /// <summary>The phone wrote to a characteristic (SimpleWrite, DdtControl, DdtData) or sent SPP bytes.</summary>
    event Action<CcuChannel, byte[]>? DataReceived;

    /// <summary>A phone connected to / disconnected from the BLE side.</summary>
    event Action<bool>? BleConnectionChanged;

    /// <summary>The Classic SPP link came up / went down.</summary>
    event Action<bool>? SppConnectionChanged;

    /// <summary>Transport-level log lines (advertising state, errors).</summary>
    event Action<string>? Diagnostic;

    bool IsRunning { get; }

    /// <summary>Start advertising / listening.</summary>
    Task StartAsync(CcuSettings settings, CancellationToken ct = default);

    Task StopAsync();

    /// <summary>Notify the phone on <see cref="CcuChannel.MainRx"/> or <see cref="CcuChannel.DdtStatus"/>.</summary>
    Task NotifyAsync(CcuChannel channel, byte[] data);

    /// <summary>The CCU opens the Classic link to the phone's SPP server. Raises <see cref="SppConnectionChanged"/>.</summary>
    Task ConnectSppAsync(CancellationToken ct = default);

    /// <summary>Send raw bytes (already SPP-framed) to the phone.</summary>
    Task SendSppAsync(byte[] data);

    /// <summary>Drop the current phone connection (fault injection).</summary>
    Task DisconnectAsync();
}
