using System.Text.Json;
using System.Text.Json.Serialization;

namespace CNCO.YccuSdk.Platform;

/// <summary>
/// <see cref="IYmcKeyValueStore"/> backed by a single JSON file. Best-effort: a missing or unreadable file is treated as empty.
/// </summary>
public sealed class JsonFileKeyValueStore : IYmcKeyValueStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, string> _values;

    public JsonFileKeyValueStore(string path)
    {
        _path = path;
        _values = Load();
    }

    public string? GetString(string key)
    {
        lock (_lock)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void SetString(string key, string value)
    {
        lock (_lock)
        {
            _values[key] = value;
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_path, JsonSerializer.Serialize(_values, KeyValueJsonContext.Default.DictionaryStringString));
            }
            catch (Exception)
            {
                // Best-effort persistence only.
            }
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(_path), KeyValueJsonContext.Default.DictionaryStringString) ?? new();
            }
        }
        catch (Exception)
        {
            // Fall through to an empty store.
        }
        return new();
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class KeyValueJsonContext : JsonSerializerContext
{
}
