namespace CNCO.YccuSdk.Tracks;

/// <summary>
/// Named text files in a few folders ("tracks", "sessions"). The platform provides the real thing; <see cref="InMemoryTrackFileStore"/>
/// is for tests and the desktop fake.
/// </summary>
public interface ITrackFileStore
{
    IReadOnlyList<string> List(string folder);

    string? Read(string folder, string name);

    void Write(string folder, string name, string text);

    /// <summary>
    /// Adds text to the end of a file (creating it) and flushes, so a crash leaves what was written.
    /// </summary>
    void Append(string folder, string name, string text);

    void Delete(string folder, string name);
}

public sealed class InMemoryTrackFileStore : ITrackFileStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _files = new();

    private static string Key(string folder, string name) => folder + "/" + name;

    public IReadOnlyList<string> List(string folder)
    {
        lock (_gate)
        {
            var prefix = folder + "/";
            return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[prefix.Length..]).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
    }

    public string? Read(string folder, string name)
    {
        lock (_gate) return _files.TryGetValue(Key(folder, name), out var text) ? text : null;
    }

    public void Write(string folder, string name, string text)
    {
        lock (_gate) _files[Key(folder, name)] = text;
    }

    public void Append(string folder, string name, string text)
    {
        lock (_gate) _files[Key(folder, name)] = (_files.TryGetValue(Key(folder, name), out var old) ? old : "") + text;
    }

    public void Delete(string folder, string name)
    {
        lock (_gate) _files.Remove(Key(folder, name));
    }
}
