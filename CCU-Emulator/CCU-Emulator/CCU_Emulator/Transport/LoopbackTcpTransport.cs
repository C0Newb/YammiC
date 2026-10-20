using System.Net;
using System.Net.Sockets;
using CCU_Emulator.Emulation;

namespace CCU_Emulator.Transport;

/// <summary>
/// Radio-free transport for tests and for driving the emulator from another process.
/// One TCP client at a time (the newest wins). Frame: <c>[channel 1B][length 2B BE][bytes]</c>.
/// SPP shares the same connection under <see cref="CcuChannel.Spp"/>.
/// </summary>
public sealed class LoopbackTcpTransport : ICcuTransport
{
    public event Action<CcuChannel, byte[]>? DataReceived;
    public event Action<bool>? BleConnectionChanged;
    public event Action<bool>? SppConnectionChanged;
    public event Action<string>? Diagnostic;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private TcpClient? _client;
    private bool _sppUp;

    public int Port { get; private set; }
    public bool IsRunning => _listener != null;

    public Task StartAsync(CcuSettings settings, CancellationToken ct = default)
    {
        if (_listener != null) return Task.CompletedTask;
        var address = IPAddress.Parse(settings.TcpBindAddress);
        _listener = new TcpListener(address, settings.TcpPort);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Diagnostic?.Invoke($"loopback transport listening on {address}:{Port}");
        _ = AcceptLoopAsync(_listener, _cts.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                OnClientGone(_client); // newest wins
                client.NoDelay = true;
                _client = client;
                Diagnostic?.Invoke("client connected");
                BleConnectionChanged?.Invoke(true);
                _ = ReadLoopAsync(client, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    private async Task ReadLoopAsync(TcpClient client, CancellationToken ct)
    {
        var header = new byte[3];
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                await stream.ReadExactlyAsync(header, ct);
                var payload = new byte[(header[1] << 8) | header[2]];
                await stream.ReadExactlyAsync(payload, ct);
                DataReceived?.Invoke((CcuChannel)header[0], payload);
            }
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        finally
        {
            OnClientGone(client);
        }
    }

    private void OnClientGone(TcpClient? client)
    {
        if (client is null) return;
        if (ReferenceEquals(_client, client))
        {
            _client = null;
            SetSpp(false);
            Diagnostic?.Invoke("client disconnected");
            BleConnectionChanged?.Invoke(false);
        }
        client.Dispose();
    }

    private void SetSpp(bool up)
    {
        if (_sppUp == up) return;
        _sppUp = up;
        SppConnectionChanged?.Invoke(up);
    }

    private async Task WriteFrameAsync(CcuChannel channel, byte[] data)
    {
        var client = _client ?? throw new InvalidOperationException("no client connected");
        if (data.Length > ushort.MaxValue) throw new ArgumentException("frame too large", nameof(data));
        var frame = new byte[3 + data.Length];
        frame[0] = (byte)channel;
        frame[1] = (byte)(data.Length >> 8);
        frame[2] = (byte)data.Length;
        data.CopyTo(frame, 3);
        await _writeLock.WaitAsync();
        try { await client.GetStream().WriteAsync(frame); }
        finally { _writeLock.Release(); }
    }

    public Task NotifyAsync(CcuChannel channel, byte[] data)
    {
        if (channel is not (CcuChannel.MainRx or CcuChannel.DdtStatus))
            throw new ArgumentException("only MainRx and DdtStatus are notified", nameof(channel));
        return WriteFrameAsync(channel, data);
    }

    public Task ConnectSppAsync(CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("no client connected");
        SetSpp(true);
        return Task.CompletedTask;
    }

    public Task SendSppAsync(byte[] data) => WriteFrameAsync(CcuChannel.Spp, data);

    public Task DisconnectAsync()
    {
        OnClientGone(_client);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        await DisconnectAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
