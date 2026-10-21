namespace CNCO.YccuSdk.Can;

/// <summary>
/// Reassembles a bike-to-phone DDT v3 session. Every indication is
/// <c>[sequence 2B big-endian][fragment index 1B, from 1][fragment count 1B][data...]</c>; when all fragments of a
/// sequence are in, they are joined in order. No acknowledgement is sent. An incomplete sequence is dropped after
/// <see cref="Timeout"/>.
/// </summary>
public sealed class CanDdtV3Assembler
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private sealed class Partial
    {
        public Partial(int count) => Fragments = new byte[]?[count];

        public byte[]?[] Fragments { get; }
        public DateTime LastSeen { get; set; }
    }

    private readonly Dictionary<ushort, Partial> _sequences = new();

    /// <summary>
    /// Feeds one indication; returns the whole payload once its last fragment arrived, otherwise null.
    /// </summary>
    public byte[]? Feed(byte[] indication, DateTime now)
    {
        Expire(now);
        if (indication.Length < 4) return null;

        var sequence = (ushort)((indication[0] << 8) | indication[1]);
        int index = indication[2];
        int count = indication[3];
        if (index < 1 || count < 1 || index > count) return null;

        if (!_sequences.TryGetValue(sequence, out var partial) || partial.Fragments.Length != count)
        {
            partial = new Partial(count);
            _sequences[sequence] = partial;
        }
        partial.LastSeen = now;
        partial.Fragments[index - 1] = indication[4..];

        if (partial.Fragments.Any(f => f == null)) return null;

        _sequences.Remove(sequence);
        return partial.Fragments.SelectMany(f => f!).ToArray();
    }

    public void Clear() => _sequences.Clear();

    private void Expire(DateTime now)
    {
        var stale = _sequences.Where(p => now - p.Value.LastSeen > Timeout).Select(p => p.Key).ToList();
        foreach (var key in stale) _sequences.Remove(key);
    }
}
