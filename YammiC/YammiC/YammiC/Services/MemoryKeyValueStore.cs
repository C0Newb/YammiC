using System.Collections.Generic;
using CNCO.YccuSdk.Platform;

namespace YammiC.Services;

/// <summary>A key/value store that lives only as long as the app runs (the simulator and the broker-only controller).</summary>
public sealed class MemoryKeyValueStore : IYmcKeyValueStore
{
    private readonly Dictionary<string, string> _values = new();

    public string? GetString(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public void SetString(string key, string value) => _values[key] = value;
}
