using CCU_Emulator.Emulation;

namespace CCU_Emulator.Transport;

public static class TransportFactory
{
    public static bool IsAvailable(TransportKind kind) => kind switch
    {
        TransportKind.LoopbackTcp => true,
#if WINDOWS_BLUETOOTH
        TransportKind.WindowsBle => true,
#endif
        _ => false,
    };

    public static ICcuTransport Create(TransportKind kind) => kind switch
    {
        TransportKind.LoopbackTcp => new LoopbackTcpTransport(),
#if WINDOWS_BLUETOOTH
        TransportKind.WindowsBle => new Platforms.Windows.WindowsBleTransport(),
#endif
        _ => throw new PlatformNotSupportedException($"Transport {kind} is not available in this build."),
    };
}
