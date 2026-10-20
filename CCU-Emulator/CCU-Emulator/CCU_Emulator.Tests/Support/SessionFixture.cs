using CCU_Emulator.Emulation;

namespace CCU_Emulator.Tests.Support;

internal sealed class SessionFixture
{
    public FakeTransport Transport { get; } = new();
    public CcuSettings Settings { get; } = new() { CcuId = "01234567890123", PassKey = "012345" };
    public CcuSession Session { get; }
    public PhoneSimulator Phone { get; }

    public SessionFixture()
    {
        Session = new CcuSession(Transport, Settings);
        Phone = new PhoneSimulator(Transport);
    }

    /// <summary>Connects the phone and authenticates with the right credentials.</summary>
    public void ConnectAndAuthenticate()
    {
        Phone.Connect();
        Phone.Authenticate(Settings.CcuId, Settings.PassKey);
    }
}
