using CCU_Emulator.Emulation;
using CCU_Emulator.Tests.Support;
using CCU_Emulator.ViewModels;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

/// <summary>Regression tests for the final whole-branch review (Important findings 1, 2 and 4).</summary>
public class ReviewFixTests
{
    // Finding 2: a ringing call that ends must not leave the dash on "Incoming call".
    [Fact]
    public void CallChangeEndedAlsoClearsRinging()
    {
        var f = new SessionFixture();
        f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.True(f.Session.State.CallRinging);

        f.Phone.WriteSimple(H("0134 02"));
        Assert.False(f.Session.State.CallRinging);
        Assert.False(f.Session.State.CallActive);
    }

    // Finding 2: YammiC ends a ring (rejected / missed) by re-sending the same 0x0118 with the missed flag clear.
    [Fact]
    public void RepeatedIncomingCallInfoForTheSameCallerEndsTheRing()
    {
        var f = new SessionFixture();
        f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.True(f.Session.State.CallRinging);

        f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.False(f.Session.State.CallRinging);

        // A later call rings again.
        f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.True(f.Session.State.CallRinging);
    }

    [Fact]
    public void AnIncomingCallFromADifferentCallerReplacesTheRingingOne()
    {
        var f = new SessionFixture();
        f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        f.Phone.SendDdt(Cat(H("0118"), Str8("Bob"), Str8("777"), H("00")));
        Assert.True(f.Session.State.CallRinging);
        Assert.Equal("Bob", f.Session.State.CallerName);
    }

    // Finding 1: pressing Start twice must not run two session clocks.
    [Fact]
    public async Task StartingTwiceRunsOnlyOneTickLoop()
    {
        var transport = new FakeTransport();
        var settings = new CcuSettings { CcuId = "01234567890123", PassKey = "012345" };
        settings.Behavior.SendDeviceTimeRequest = false;
        settings.Behavior.PingIntervalSeconds = 1;
        await using var host = new EmulatorHost(settings, Path.GetTempFileName(), null, _ => transport);

        await host.StartAsync();
        await host.StartAsync();
        transport.RaiseBle(true);
        new PhoneSimulator(transport).Authenticate(settings.CcuId, settings.PassKey);

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        var pings = transport.MainRx.Count(p => p.Length == 3 && p[0] == 0x01 && p[1] == 0x35);
        var seqs = string.Join(",", transport.MainRx.Where(p => p.Length == 3 && p[1] == 0x35).Select(p => p[2]));
        Assert.True(pings <= 2, $"expected at most 2 pings from one tick loop, got {pings} (seq {seqs})");
    }

    // Finding 4: "Drop connection" must report a transport failure, not crash the app.
    [Fact]
    public async Task DropReportsATransportFailureInsteadOfThrowing()
    {
        var transport = new FakeTransport { OnDisconnect = () => throw new InvalidOperationException("rebuild failed") };
        var host = new EmulatorHost(new CcuSettings(), Path.GetTempFileName(), null, _ => transport);
        var vm = new ConnectionViewModel(host);

        await vm.DropCommand.ExecuteAsync(null);
        Assert.Contains("rebuild failed", vm.LastError);
    }
}
