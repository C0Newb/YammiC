namespace CNCO.YccuSdk.Can;

/// <summary>
/// One CAN signal as the bike reported it.
/// </summary>
public readonly record struct CanReading(int Id, byte[] Data);

/// <summary>
/// Reads a CAN response: <c>[cmd 2B: 0x0114][payloadLen 3B][protobuf]</c>, where the protobuf has repeated field 3
/// (<c>data_list</c>) messages holding field 1 (<c>id</c>, varint) and field 2 (<c>data</c>, bytes). Unknown fields
/// are skipped.
/// </summary>
public static class CanResponseParser
{
    public static IReadOnlyList<CanReading>? Parse(byte[] payload)
    {
        if (payload.Length < 5 || payload[0] != 0x01 || payload[1] != 0x14) return null;

        var readings = new List<CanReading>();
        var pos = 5;
        try
        {
            while (pos < payload.Length)
            {
                var tag = (int)ReadVarint(payload, ref pos);
                var field = tag >> 3;
                var wire = tag & 7;
                if (field == 3 && wire == 2)
                {
                    var length = (int)ReadVarint(payload, ref pos);
                    var entry = ParseEntry(payload, pos, length);
                    if (entry != null) readings.Add(entry.Value);
                    pos += length;
                }
                else
                {
                    Skip(payload, ref pos, wire);
                }
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or FormatException)
        {
            return null;
        }
        return readings;
    }

    private static CanReading? ParseEntry(byte[] buffer, int start, int length)
    {
        var pos = start;
        var end = start + length;
        var id = -1;
        byte[] data = Array.Empty<byte>();
        while (pos < end)
        {
            var tag = (int)ReadVarint(buffer, ref pos);
            var field = tag >> 3;
            var wire = tag & 7;
            if (field == 1 && wire == 0)
            {
                id = (int)ReadVarint(buffer, ref pos);
            }
            else if (field == 2 && wire == 2)
            {
                var size = (int)ReadVarint(buffer, ref pos);
                data = buffer[pos..(pos + size)];
                pos += size;
            }
            else
            {
                Skip(buffer, ref pos, wire);
            }
        }
        return id < 0 ? null : new CanReading(id, data);
    }

    private static ulong ReadVarint(byte[] buffer, ref int pos)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = buffer[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new FormatException("Varint too long");
    }

    private static void Skip(byte[] buffer, ref int pos, int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(buffer, ref pos); break;
            case 1: pos += 8; break;
            case 2: pos += (int)ReadVarint(buffer, ref pos); break;
            case 5: pos += 4; break;
            default: throw new FormatException("Unsupported wire type");
        }
    }
}
