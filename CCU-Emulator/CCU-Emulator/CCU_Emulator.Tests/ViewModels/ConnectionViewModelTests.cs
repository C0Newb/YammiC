using CCU_Emulator.Emulation;
using CCU_Emulator.Tests.Support;
using CCU_Emulator.ViewModels;
using Xunit;

namespace CCU_Emulator.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static (EmulatorHost host, FakeTransport transport) CreateHost(CcuSettings? settings = null)
    {
        var transport = new FakeTransport();
        var host = new EmulatorHost(settings ?? new CcuSettings(), Path.GetTempFileName(), null, _ => transport);
        return (host, transport);
    }

    [Fact]
    public async Task StartAndStopToggleRunning()
    {
        var (host, transport) = CreateHost();
        var vm = new ConnectionViewModel(host);
        Assert.Equal("Stopped", vm.LinkText);

        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.Running);
        Assert.True(transport.IsRunning);
        Assert.Equal("Advertising / waiting for a phone", vm.LinkText);

        transport.RaiseBle(true);
        Assert.Equal("Phone connected (not authenticated)", vm.LinkText);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.Running);
    }

    [Fact]
    public async Task StartWithAnInvalidIdentityShowsTheError()
    {
        var (host, _) = CreateHost(new CcuSettings { CcuId = "bad" });
        var vm = new ConnectionViewModel(host);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(vm.Running);
        Assert.Contains("CcuId", vm.LastError);
    }

    [Fact]
    public async Task DropDisconnectsThePhone()
    {
        var (host, transport) = CreateHost();
        var vm = new ConnectionViewModel(host);
        await vm.DropCommand.ExecuteAsync(null);
        Assert.Equal(1, transport.DisconnectCalls);
    }
}
