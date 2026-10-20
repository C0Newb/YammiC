using System.Collections.ObjectModel;

namespace CCU_Emulator.Emulation;

public enum Direction { Rx, Tx, Info }

public sealed record PacketLogEntry(DateTime Time, Direction Direction, string Channel, string Name, string Hex, string Detail);

/// <summary>Capped, observable packet log. Mutate only from the thread the UI binds on.</summary>
public sealed class PacketLog
{
    public const int Capacity = 2000;
    private const int MaxHexBytes = 96;

    public ObservableCollection<PacketLogEntry> Entries { get; } = [];

    public void Add(Direction direction, string channel, string name, byte[] bytes, string detail = "")
        => Append(new PacketLogEntry(DateTime.Now, direction, channel, name, ToHex(bytes), detail));

    public void Info(string source, string message)
        => Append(new PacketLogEntry(DateTime.Now, Direction.Info, source, "", "", message));

    public void Clear() => Entries.Clear();

    private void Append(PacketLogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > Capacity) Entries.RemoveAt(0);
    }

    public static string ToHex(byte[] bytes)
    {
        var shown = bytes.Length > MaxHexBytes ? bytes[..MaxHexBytes] : bytes;
        var hex = string.Join(' ', shown.Select(b => b.ToString("X2")));
        return bytes.Length > MaxHexBytes ? $"{hex} …({bytes.Length} bytes)" : hex;
    }
}
