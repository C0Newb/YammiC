#if WINDOWS_BLUETOOTH
using System.Runtime.InteropServices.WindowsRuntime;
using CCU_Emulator.Emulation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace CCU_Emulator.Transport.Platforms.Windows;

/// <summary>
/// The CCU's BLE side on Windows: a GATT server with the characteristics from AGENTS.md.
/// Phone writes become <see cref="DataReceived"/>; <see cref="NotifyAsync"/> notifies subscribed phones.
/// </summary>
public sealed class WindowsBleTransport : ICcuTransport
{
    private sealed record Layout(string Uuid, GattCharacteristicProperties Properties, CcuChannel? Channel);

    // Properties follow what a CCU exposes as far as known; adjust from the tools/BtsnoopDump output (Task 14, step 3).
    private static readonly Layout[] Characteristics =
    [
        new("B606C7F9-E5A1-4E75-B313-2A920054A8EB", GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, CcuChannel.SimpleWrite),
        new("9C810D26-B605-4306-8C1C-755A1BA3066C", GattCharacteristicProperties.Notify, CcuChannel.MainRx),
        new("56851826-A090-4F6A-A5DF-D1776520F74A", GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, CcuChannel.DdtControl),
        new("75AB2788-87F6-4F61-BA9C-6F85A12600D1", GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify, CcuChannel.DdtStatus),
        new("E998AEAB-EA8F-4063-83AA-2017C1A44484", GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, CcuChannel.DdtData),
        new("AB8D233A-CC58-4932-B397-A3751911E021", GattCharacteristicProperties.Notify, null),   // YRC settings RX
        new("77808451-E42F-4F6B-83ED-3AB44AA3D16A", GattCharacteristicProperties.Notify, null),   // DDT hi-bandwidth
        new("347456EE-A588-4DD5-BD98-62BE3672E27A", GattCharacteristicProperties.Notify | GattCharacteristicProperties.Write, null),   // DDT large
        new("E974B9A1-FA31-43C6-91AE-09931F3F6EF1", GattCharacteristicProperties.Notify, null),   // DDT v2
        new("56DC2D58-CBE1-4BA9-A104-E9B8EB4DA95C", GattCharacteristicProperties.Indicate, null), // DDT v3
        new("7690EEF4-F3A1-11EC-B939-0242AC120002", GattCharacteristicProperties.Notify, null),   // YRC response RX
    ];

    private const string ServiceUuid = "AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A";

    public event Action<CcuChannel, byte[]>? DataReceived;
    public event Action<bool>? BleConnectionChanged;
    public event Action<bool>? SppConnectionChanged;
    public event Action<string>? Diagnostic;

    private readonly Dictionary<CcuChannel, GattLocalCharacteristic> _notifyChars = [];
    private readonly List<GattLocalCharacteristic> _allChars = [];
    private GattServiceProvider? _provider;
    private CcuSettings? _settings;
    private WindowsSppClient? _spp;
    private byte[] _lastStatus = [0, 0];
    private bool _connected;

    public bool IsRunning => _provider != null;

    public async Task StartAsync(CcuSettings settings, CancellationToken ct = default)
    {
        if (_provider != null) return;
        _settings = settings;

        var adapter = await BluetoothAdapter.GetDefaultAsync();
        if (adapter is null) throw new InvalidOperationException("No Bluetooth adapter found.");
        if (!adapter.IsPeripheralRoleSupported) throw new InvalidOperationException("This Bluetooth adapter does not support the peripheral role.");

        var created = await GattServiceProvider.CreateAsync(Guid.Parse(ServiceUuid));
        if (created.Error != BluetoothError.Success) throw new InvalidOperationException($"GATT service creation failed: {created.Error}");
        var provider = created.ServiceProvider;

        foreach (var layout in Characteristics)
        {
            var parameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = layout.Properties,
                ReadProtectionLevel = GattProtectionLevel.Plain,
                WriteProtectionLevel = GattProtectionLevel.Plain,
            };
            var result = await provider.Service.CreateCharacteristicAsync(Guid.Parse(layout.Uuid), parameters);
            if (result.Error != BluetoothError.Success) throw new InvalidOperationException($"Characteristic {layout.Uuid} failed: {result.Error}");

            var characteristic = result.Characteristic;
            _allChars.Add(characteristic);
            if (layout.Channel is { } channel)
            {
                if (layout.Properties.HasFlag(GattCharacteristicProperties.Notify)) _notifyChars[channel] = characteristic;
                if (layout.Properties.HasFlag(GattCharacteristicProperties.Write))
                    characteristic.WriteRequested += (s, e) => OnWrite(e, channel);
                if (layout.Properties.HasFlag(GattCharacteristicProperties.Read))
                    characteristic.ReadRequested += (s, e) => OnRead(e);
            }
            characteristic.SubscribedClientsChanged += (s, _) => OnSubscribersChanged();
        }

        provider.AdvertisementStatusChanged += (_, e) => Diagnostic?.Invoke($"advertisement: {e.Status}");
        provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
        _provider = provider;
        Diagnostic?.Invoke($"GATT server started; the advertised name is {settings.AdvertisedName} (see Task 15 for how Windows exposes it)");
    }

    private async void OnWrite(GattWriteRequestedEventArgs args, CcuChannel channel)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null) return;
            var bytes = request.Value.ToArray();
            if (request.Option == GattWriteOption.WriteWithResponse) request.Respond();
            DataReceived?.Invoke(channel, bytes);
        }
        catch (Exception e)
        {
            Diagnostic?.Invoke($"write on {channel} failed: {e.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnRead(GattReadRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            request?.RespondWithValue(_lastStatus.AsBuffer());   // the only readable characteristic is DDT status
        }
        catch (Exception e)
        {
            Diagnostic?.Invoke($"read failed: {e.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>The phone counts as connected while any characteristic has a subscriber.</summary>
    private void OnSubscribersChanged()
    {
        var any = _allChars.Any(c => c.SubscribedClients.Count > 0);
        if (any == _connected) return;
        _connected = any;
        if (!any) _lastStatus = [0, 0];   // a dropped phone leaves no DDT session behind
        Diagnostic?.Invoke(any ? "phone subscribed" : "phone unsubscribed / disconnected");
        BleConnectionChanged?.Invoke(any);
    }

    public async Task NotifyAsync(CcuChannel channel, byte[] data)
    {
        if (channel == CcuChannel.DdtStatus) _lastStatus = data;
        if (!_notifyChars.TryGetValue(channel, out var characteristic))
            throw new ArgumentException($"{channel} is not a notify characteristic", nameof(channel));

        var results = await characteristic.NotifyValueAsync(data.AsBuffer());
        if (results.Count == 0) throw new InvalidOperationException("no subscribed client");
        foreach (var result in results)
            if (result.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"notify failed: {result.Status}");
    }

    public async Task ConnectSppAsync(CancellationToken ct = default)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_settings.PhoneBluetoothAddress))
            throw new InvalidOperationException("PhoneBluetoothAddress is not set in the settings.");
        _spp?.Dispose();
        _spp = new WindowsSppClient();
        _spp.DataReceived += data => DataReceived?.Invoke(CcuChannel.Spp, data);
        _spp.ConnectionChanged += up => SppConnectionChanged?.Invoke(up);
        _spp.Diagnostic += message => Diagnostic?.Invoke(message);
        await _spp.ConnectAsync(_settings.PhoneBluetoothAddress, ct);
    }

    public Task SendSppAsync(byte[] data)
        => _spp?.WriteAsync(data) ?? throw new InvalidOperationException("SPP link is not connected");

    /// <summary>
    /// Windows cannot force-disconnect a central. Best effort: close SPP, then tear the GATT service
    /// down and rebuild it, which drops the existing link. Verify on hardware (Task 18).
    /// </summary>
    public async Task DisconnectAsync()
    {
        _spp?.Dispose();
        _spp = null;
        if (_settings is null || _provider is null) return;
        var settings = _settings;
        await StopAsync();
        await StartAsync(settings);
    }

    public Task StopAsync()
    {
        _spp?.Dispose();
        _spp = null;
        _provider?.StopAdvertising();
        _provider = null;
        _notifyChars.Clear();
        _allChars.Clear();
        _lastStatus = [0, 0];
        if (_connected)
        {
            _connected = false;
            BleConnectionChanged?.Invoke(false);
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
#endif
