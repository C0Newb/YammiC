namespace CCU_Emulator.Protocol;

/// <summary>Decodes the hand-encoded protobuf inside NotificationDataV2 (docs/notifications.md).</summary>
internal static class NotificationProto
{
    public static NotificationMessage Decode(byte[] proto)
    {
        var r = new PacketReader(proto);
        int id = 0, category = 0, year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
        string title = "", subTitle = "", detail = "";

        while (r.Remaining > 0)
        {
            var tag = (int)r.Varint();
            var field = tag >> 3;
            var wire = tag & 7;
            switch (wire)
            {
                case 0:
                {
                    var v = (int)r.Varint();
                    switch (field)
                    {
                        case 3: id = v; break;
                        case 4: category = v; break;
                        case 5: year = v; break;
                        case 6: month = v; break;
                        case 7: day = v; break;
                        case 8: hour = v; break;
                        case 9: minute = v; break;
                        case 10: second = v; break;
                    }
                    break;
                }
                case 2:
                {
                    var text = r.Utf8((int)r.Varint());
                    switch (field)
                    {
                        case 11: title = text; break;
                        case 12: subTitle = text; break;
                        case 13: detail = text; break;
                    }
                    break;
                }
                default:
                    throw new MalformedPacketException($"unsupported protobuf wire type {wire}");
            }
        }

        DateTime? timestamp = null;
        try { if (year > 0) timestamp = new DateTime(year, month, day, hour, minute, second); }
        catch (ArgumentOutOfRangeException) { /* leave the timestamp unset */ }

        return new NotificationMessage(id, category, timestamp, title, subTitle, detail);
    }
}
