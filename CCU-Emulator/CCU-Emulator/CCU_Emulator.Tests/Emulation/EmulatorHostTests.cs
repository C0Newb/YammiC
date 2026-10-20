using System.Net;
using System.Net.Sockets;
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class EmulatorHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CcuSettings Settings() => new() { CcuId = "01234567890123", PassKey = "012345", TcpPort = 0 };

    /// <summary>Tiny scripted phone speaking the loopback framing.</summary>
    private sealed class ScriptedPhone(TcpClient client)
    {
        private readonly NetworkStream _stream = client.GetStream();
        private byte _reqId = 1;

        public async Task WriteAsync(CcuChannel role, byte[] data)
            => await _stream.WriteAsync(Cat([(byte)role, (byte)(data.Length >> 8), (byte)data.Length], data));

        public async Task SendDdtAsync(byte[] message)
        {
            var id = _reqId++;
            await WriteAsync(CcuChannel.DdtControl, [id, 0, (byte)(message.Length >> 8), (byte)message.Length]);
            await WriteAsync(CcuChannel.DdtData, Cat([id, 0, 0, 0], message));
        }

        public async Task<(CcuChannel Role, byte[] Data)> ReadFrameAsync()
        {
            var header = new byte[3];
            await _stream.ReadExactlyAsync(header).AsTask().WaitAsync(Timeout);
            var data = new byte[(header[1] << 8) | header[2]];
            await _stream.ReadExactlyAsync(data).AsTask().WaitAsync(Timeout);
            return ((CcuChannel)header[0], data);
        }

        public async Task<byte[]> ReadMainRxAsync()
        {
            while (true)
            {
                var (role, data) = await ReadFrameAsync();
                if (role == CcuChannel.MainRx) return data;
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ScriptedPhoneAuthenticatesAndDrivesTheEmulatorOverLoopback()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccu-host-{Guid.NewGuid():N}.json");
        await using var host = new EmulatorHost(Settings(), path);
        await host.StartAsync();
        Assert.True(host.IsRunning);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((LoopbackTcpTransport)host.Transport).Port);
        var phone = new ScriptedPhone(client);

        await phone.SendDdtAsync(Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")));
        Assert.Equal(H("0102 01"), await phone.ReadMainRxAsync());
        Assert.Equal(H("0001 030101"), await phone.ReadMainRxAsync());   // LibVersion follows auth

        await phone.SendDdtAsync(Cat(H("0116"), Str8("Artist"), Str8("Album"), Str8("Song"), H("0000D2 02 000A 00000F")));
        await WaitUntilAsync(() => host.Session.State.MusicSong == "Song");

        host.Session.Press(MusicAction.Next);
        // Drain until the music-control frame appears (startup requests arrive first).
        byte[] frame;
        do { frame = await phone.ReadMainRxAsync(); } while (!frame.SequenceEqual(H("0117 01")));
    }

    [Fact]
    public async Task DisconnectMidSessionResetsAuthentication()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccu-host-{Guid.NewGuid():N}.json");
        await using var host = new EmulatorHost(Settings(), path);
        await host.StartAsync();

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, ((LoopbackTcpTransport)host.Transport).Port);
            var phone = new ScriptedPhone(client);
            await phone.SendDdtAsync(Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")));
            await WaitUntilAsync(() => host.Session.State.Authenticated);
        }

        await WaitUntilAsync(() => !host.Session.State.BleConnected);
        Assert.False(host.Session.State.Authenticated);
    }

    [Fact]
    public async Task InvalidIdentityRefusesToStart()
    {
        var settings = Settings();
        settings.CcuId = "short";
        await using var host = new EmulatorHost(settings, Path.GetTempFileName());
        await host.StartAsync();
        Assert.False(host.IsRunning);
        Assert.Contains("CcuId", host.LastError);
    }

    [Fact]
    public void LoopbackTransportIsAlwaysAvailable()
    {
        Assert.True(TransportFactory.IsAvailable(TransportKind.LoopbackTcp));
    }
}
