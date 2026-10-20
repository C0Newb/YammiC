using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk;

internal class YmcBikeSessionStore
{
    private const string KeyDeviceName = "device_name";
    private const string KeyCcuId = "ccu_id";
    private const string KeyPassKey = "pass_key";
    private const string KeyPhoneUuid = "phone_uuid";
    private const string KeyBikeAddress = "bike_address";

    // FOR THE LOVE OF GOD DO NOT COMMIT THIS!!!
    // Test defaults carried over from the Kotlin app so a fresh install can connect without typing them in.
    private const string DefaultDeviceName = "YCCU_01234567890123";
    private const string DefaultCcuId = "01234567890123";
    private const string DefaultPassKey = "012345";
    private const string DefaultPhoneUuid = "0123456789abcdef0123456789abcdef";

    private readonly IYmcKeyValueStore _store;

    public YmcBikeSessionStore(IYmcKeyValueStore store)
    {
        _store = store;
    }

    public BikeDebugState LoadState(string logFileLabel) =>
        new()
        {
            DeviceName = _store.GetString(KeyDeviceName) ?? DefaultDeviceName,
            CcuId = _store.GetString(KeyCcuId) ?? DefaultCcuId,
            PassKey = _store.GetString(KeyPassKey) ?? DefaultPassKey,
            PhoneUuid = _store.GetString(KeyPhoneUuid) ?? DefaultPhoneUuid,
            LogFileLabel = logFileLabel,
        };

    public void SaveCredentials(string ccuId, string passKey)
    {
        _store.SetString(KeyCcuId, ccuId.Trim());
        _store.SetString(KeyPassKey, passKey.Trim());
    }

    public void SaveDeviceName(string deviceName) => _store.SetString(KeyDeviceName, deviceName.Trim());

    /// <summary>The bike's Bluetooth address from the last successful scan (used to recognise it when it connects to the phone).</summary>
    public string? BikeAddress => _store.GetString(KeyBikeAddress);

    public void SaveBikeAddress(string address) => _store.SetString(KeyBikeAddress, address.Trim());

    public void SavePhoneUuid(string phoneUuid) => _store.SetString(KeyPhoneUuid, phoneUuid.Trim());
}
