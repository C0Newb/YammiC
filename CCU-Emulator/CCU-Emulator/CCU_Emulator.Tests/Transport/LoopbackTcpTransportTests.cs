using System.Net;
using System.Net.Sockets;
using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;
using Xunit;

namespace CCU_Emulator.Tests.Transport;

public class LoopbackTcpTransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static async Task<(LoopbackTcpTransport transport, TcpClient client)> ConnectedPairAsync()
    {
        var transport = new LoopbackTcpTransport();
        var connected = new TaskCompletionSource<bool>();
        transport.BleConnectionChanged += c => { if (c) connected.TrySetResult(true); };
        await transport.StartAsync(new CcuSettings { TcpPort = 0 });
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, transport.Port);
        await connected.Task.WaitAsync(Timeout);
        return (transport, client);
    }

    [Fact]
    public async Task FramesFlowBothWays()
    {
        var (transport, client) = await ConnectedPairAsync();
        await using var _ = transport;
        using var __ = client;
        var received = new TaskCompletionSource<(CcuChannel, byte[])>();
        transport.DataReceived += (ch, d) => received.TrySetResult((ch, d));

        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 1, 0, 3, 9, 8, 7 });
        var (channel, data) = await received.Task.WaitAsync(Timeout);
        Assert.Equal(CcuChannel.SimpleWrite, channel);
        Assert.Equal(new byte[] { 9, 8, 7 }, data);

        await transport.NotifyAsync(CcuChannel.MainRx, new byte[] { 1, 2 });
        var buf = new byte[5];
        await stream.ReadExactlyAsync(buf).AsTask().WaitAsync(Timeout);
        Assert.Equal(new byte[] { 2, 0, 2, 1, 2 }, buf);
    }

    [Fact]
    public async Task SppSharesTheConnection()
    {
        var (transport, client) = await ConnectedPairAsync();
        await using var _ = transport;
        using var __ = client;
        var sppUp = new TaskCompletionSource<bool>();
        transport.SppConnectionChanged += up => { if (up) sppUp.TrySetResult(true); };

        await transport.ConnectSppAsync();
        await sppUp.Task.WaitAsync(Timeout);

        await transport.SendSppAsync(new byte[] { 0xAA });
        var buf = new byte[4];
        await client.GetStream().ReadExactlyAsync(buf).AsTask().WaitAsync(Timeout);
        Assert.Equal(new byte[] { 6, 0, 1, 0xAA }, buf);
    }

    [Fact]
    public async Task ClientDisconnectRaisesBleAndSppDown()
    {
        var (transport, client) = await ConnectedPairAsync();
        await using var _ = transport;
        await transport.ConnectSppAsync();
        var bleDown = new TaskCompletionSource<bool>();
        var sppDown = new TaskCompletionSource<bool>();
        transport.BleConnectionChanged += up => { if (!up) bleDown.TrySetResult(true); };
        transport.SppConnectionChanged += up => { if (!up) sppDown.TrySetResult(true); };

        client.Dispose();
        await bleDown.Task.WaitAsync(Timeout);
        await sppDown.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task NotifyWithNoClientThrows()
    {
        await using var transport = new LoopbackTcpTransport();
        await transport.StartAsync(new CcuSettings { TcpPort = 0 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.NotifyAsync(CcuChannel.MainRx, [1]));
    }
}
