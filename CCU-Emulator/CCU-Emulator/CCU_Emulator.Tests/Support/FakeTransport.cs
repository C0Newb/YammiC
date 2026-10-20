using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;

namespace CCU_Emulator.Tests.Support;

#pragma warning disable CS0067 // Diagnostic is part of the interface but never raised by the fake
internal sealed class FakeTransport : ICcuTransport
{
    public event Action<CcuChannel, byte[]>? DataReceived;
    public event Action<bool>? BleConnectionChanged;
    public event Action<bool>? SppConnectionChanged;
    public event Action<string>? Diagnostic;

    public List<(CcuChannel Channel, byte[] Data)> Notifications { get; } = [];
    public List<byte[]> SppSent { get; } = [];
    public int SppConnectCalls { get; private set; }
    public int DisconnectCalls { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>Everything notified on the main RX characteristic, in order.</summary>
    public List<byte[]> MainRx => Notifications.Where(n => n.Channel == CcuChannel.MainRx).Select(n => n.Data).ToList();

    public List<byte[]> DdtStatus => Notifications.Where(n => n.Channel == CcuChannel.DdtStatus).Select(n => n.Data).ToList();

    public Task StartAsync(CcuSettings settings, CancellationToken ct = default) { IsRunning = true; return Task.CompletedTask; }
    public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }
    public Task NotifyAsync(CcuChannel channel, byte[] data) { Notifications.Add((channel, data)); return Task.CompletedTask; }
    public Task ConnectSppAsync(CancellationToken ct = default) { SppConnectCalls++; return Task.CompletedTask; }
    public Task SendSppAsync(byte[] data) { SppSent.Add(data); return Task.CompletedTask; }
    /// <summary>Test hook: runs inside DisconnectAsync (for example to throw).</summary>
    public Func<Task>? OnDisconnect { get; set; }

    public Task DisconnectAsync() { DisconnectCalls++; return OnDisconnect?.Invoke() ?? Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseData(CcuChannel channel, byte[] data) => DataReceived?.Invoke(channel, data);
    public void RaiseBle(bool up) => BleConnectionChanged?.Invoke(up);
    public void RaiseSpp(bool up) => SppConnectionChanged?.Invoke(up);
}
