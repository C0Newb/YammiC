using System.Text.Json;
using System.Text.Json.Serialization;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Yrc;

/// <summary>The file format for saved and exported ride modes (also what the app keeps on the phone).</summary>
public sealed class RideModeFile
{
    public const string FormatName = "YammiC ride modes";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public List<SavedRideMode> Profiles { get; set; } = new();
}

/// <summary>One saved ride mode.</summary>
public sealed class SavedRideMode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>What the rider calls this entry in the library.</summary>
    public string Label { get; set; } = "";

    public DateTime Saved { get; set; } = DateTime.Now;

    /// <summary>Numeric mode code (for example 28 = Track 1).</summary>
    public int Mode { get; set; }

    /// <summary>For people reading the file; ignored on import.</summary>
    public string ModeName { get; set; } = "";

    public int SavedNo { get; set; }
    public int ModeType { get; set; }

    /// <summary>The name shown on the bike (at most 10 characters).</summary>
    public string Name { get; set; } = "";

    public List<SavedRideValue> Values { get; set; } = new();

    public YrcProfile ToProfile() =>
        new(Mode, SavedNo, ModeType, YrcCodec.Truncate(Name), Values.Select(v => new YrcValue(v.Kind, v.Current, v.Min, v.Max)).ToList());

    public static SavedRideMode From(YrcProfile p, string label) => new()
    {
        Label = label,
        Mode = p.Mode,
        ModeName = YrcNames.Mode(p.Mode),
        SavedNo = p.SavedNo,
        ModeType = p.ModeType,
        Name = p.Name,
        Values = p.Values.Select(v => new SavedRideValue { Kind = v.Kind, Setting = YrcNames.Parameter(v.Kind), Current = v.Current, Min = v.Min, Max = v.Max }).ToList(),
    };
}

public sealed class SavedRideValue
{
    public int Kind { get; set; }

    /// <summary>For people reading the file; ignored on import.</summary>
    public string Setting { get; set; } = "";

    public int Current { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RideModeFile))]
internal partial class RideModeJsonContext : JsonSerializerContext
{
}

/// <summary>The rider's saved ride modes: kept on the phone, exported to and imported from JSON files.</summary>
public sealed class ProfileLibrary
{
    private const string Key = "ride_mode_library";

    private readonly IYmcKeyValueStore _storage;
    private readonly object _gate = new();
    private List<SavedRideMode> _items;

    public ProfileLibrary(IYmcKeyValueStore storage)
    {
        _storage = storage;
        var json = storage.GetString(Key);
        _items = (string.IsNullOrEmpty(json) ? null : TryParse(json, out _))?.Profiles ?? new List<SavedRideMode>();
    }

    public event Action? Changed;

    public IReadOnlyList<SavedRideMode> Items
    {
        get
        {
            lock (_gate) return _items.ToList();
        }
    }

    public SavedRideMode Add(YrcProfile profile, string label)
    {
        var item = SavedRideMode.From(profile, label);
        lock (_gate) _items.Add(item);
        Save();
        return item;
    }

    public void Remove(string id)
    {
        lock (_gate) _items.RemoveAll(i => i.Id == id);
        Save();
    }

    public void Rename(string id, string label)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null) item.Label = label;
        }
        Save();
    }

    /// <summary>The whole library, or only the given entries, as the JSON file contents.</summary>
    public string Export(IReadOnlyCollection<string>? ids = null)
    {
        var file = new RideModeFile();
        lock (_gate) file.Profiles = _items.Where(i => ids == null || ids.Contains(i.Id)).ToList();
        return JsonSerializer.Serialize(file, RideModeJsonContext.Default.RideModeFile);
    }

    /// <summary>Adds the entries of an exported file. Returns how many were added; <paramref name="message"/> explains problems.</summary>
    public int Import(string json, out string message)
    {
        var file = TryParse(json, out message);
        if (file == null) return 0;

        var added = 0;
        var skipped = new List<string>();
        foreach (var entry in file.Profiles)
        {
            var problem = Check(entry);
            if (problem != null)
            {
                skipped.Add($"{(string.IsNullOrEmpty(entry.Label) ? entry.Name : entry.Label)}: {problem}");
                continue;
            }
            entry.Id = Guid.NewGuid().ToString("N"); // never collide with what is already here
            entry.Name = YrcCodec.Truncate(entry.Name);
            entry.ModeName = YrcNames.Mode(entry.Mode);
            lock (_gate) _items.Add(entry);
            added++;
        }
        if (added > 0) Save();
        message = skipped.Count == 0 ? $"Imported {added}" : $"Imported {added}, skipped {skipped.Count}: {string.Join("; ", skipped)}";
        return added;
    }

    /// <summary>Why an entry cannot be used, or null if it looks fine.</summary>
    public static string? Check(SavedRideMode entry)
    {
        if (entry.Mode is < 16 or > 46) return $"unknown mode {entry.Mode}";
        if (entry.Values.Count == 0) return "no settings";
        foreach (var v in entry.Values)
        {
            if (v.Kind <= 0) return $"unknown setting {v.Kind}";
            if (v.Max > v.Min && (v.Current < v.Min || v.Current > v.Max))
                return $"{YrcNames.Parameter(v.Kind)} is {v.Current}, outside {v.Min} to {v.Max}";
        }
        return null;
    }

    /// <summary>
    /// Checks a ride mode against what the bike reported for the same mode: every setting must exist there and fit its range.
    /// Returns the problems (empty when it is safe to send).
    /// </summary>
    public static List<string> CheckAgainstBike(YrcProfile profile, IReadOnlyList<YrcProfile> bike)
    {
        var problems = new List<string>();
        var target = bike.FirstOrDefault(b => b.Mode == profile.Mode && b.SavedNo == profile.SavedNo)
                     ?? bike.FirstOrDefault(b => b.Mode == profile.Mode);
        if (target == null)
        {
            problems.Add($"The bike has no {YrcNames.Mode(profile.Mode)} (read it from the bike first)");
            return problems;
        }
        foreach (var v in profile.Values)
        {
            var onBike = target.Values.FirstOrDefault(x => x.Kind == v.Kind);
            if (onBike == null) problems.Add($"{YrcNames.Parameter(v.Kind)} does not exist on this bike");
            else if (onBike.Max > onBike.Min && (v.Current < onBike.Min || v.Current > onBike.Max))
                problems.Add($"{YrcNames.Parameter(v.Kind)} {v.Current} is outside {onBike.Min} to {onBike.Max}");
        }
        return problems;
    }

    private static RideModeFile? TryParse(string json, out string message)
    {
        message = "";
        try
        {
            var file = JsonSerializer.Deserialize(json, RideModeJsonContext.Default.RideModeFile);
            if (file == null || file.Format != RideModeFile.FormatName)
            {
                message = "This is not a YammiC ride modes file";
                return null;
            }
            if (file.Version > 1)
            {
                message = $"This file is from a newer version of the app ({file.Version})";
                return null;
            }
            return file;
        }
        catch (JsonException e)
        {
            message = "Could not read the file: " + e.Message;
            return null;
        }
    }

    private void Save()
    {
        string json;
        lock (_gate) json = JsonSerializer.Serialize(new RideModeFile { Profiles = _items }, RideModeJsonContext.Default.RideModeFile);
        _storage.SetString(Key, json);
        Changed?.Invoke();
    }
}

