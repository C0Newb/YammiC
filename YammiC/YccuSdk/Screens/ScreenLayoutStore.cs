using System.Text.Json;
using System.Text.Json.Serialization;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Screens;

[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScreenLayout))]
internal partial class ScreenJsonContext : JsonSerializerContext
{
}

/// <summary>Saves each target's layout (as JSON) in the platform key-value store; falls back to <see cref="ScreenDefaults"/>.</summary>
public sealed class ScreenLayoutStore
{
    private readonly IYmcKeyValueStore _storage;

    public ScreenLayoutStore(IYmcKeyValueStore storage)
    {
        _storage = storage;
    }

    private static string Key(ScreenTarget target) => target == ScreenTarget.Navigation ? "screen_navigation" : "screen_vpb";

    /// <summary>The saved layout, or null if none was saved or it can't be read (unlike <see cref="Load"/>, never the built-in one).</summary>
    public ScreenLayout? LoadSaved(ScreenTarget target)
    {
        var json = _storage.GetString(Key(target));
        if (string.IsNullOrEmpty(json)) return null;
        var layout = TryParse(json);
        if (layout != null) layout.Target = target;
        return layout;
    }

    /// <summary>The saved layout, or the default if none was saved or it can't be read. Always a fresh copy.</summary>
    public ScreenLayout Load(ScreenTarget target)
    {
        var json = _storage.GetString(Key(target));
        if (!string.IsNullOrEmpty(json))
        {
            var layout = TryParse(json);
            if (layout != null)
            {
                layout.Target = target;
                return layout;
            }
        }
        return ScreenDefaults.For(target);
    }

    public void Save(ScreenTarget target, ScreenLayout layout)
    {
        layout.Target = target;
        _storage.SetString(Key(target), ToJson(layout));
    }

    /// <summary>Forgets the saved layout; <see cref="Load"/> returns the default again.</summary>
    public void Reset(ScreenTarget target) => _storage.SetString(Key(target), "");

    public static string ToJson(ScreenLayout layout) =>
        JsonSerializer.Serialize(layout, ScreenJsonContext.Default.ScreenLayout);

    /// <summary>Parses a layout from JSON (e.g. an imported file); null if it isn't one.</summary>
    public static ScreenLayout? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ScreenJsonContext.Default.ScreenLayout);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
