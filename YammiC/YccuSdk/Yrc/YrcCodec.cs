using System.Text;

namespace CNCO.YccuSdk.Yrc;

/// <summary>
/// Reads and writes the ride-settings protobufs.
/// <para>
/// Info message: 1 data_kind (repeated enum), 2 result, 3 kw_status_flag, 4 yccs_model_flag, 5 yccs_at_flag,
/// 6 track_flag, 7 sus_info, 8 current_yrc_mode, 9 current_sus_mode, 10 start_trigger, 11 settings (repeated).
/// Settings: 12 mode, 13 app_saved_no, 14 yrc_mode_type, 15 name, 16 values (repeated).
/// Value: 17 kind, 18 current, 19 min, 20 max.
/// </para>
/// <para>
/// Request message: 1 data_kind (repeated), 2 details (repeated mode), 3 yrc_mode_type, 4 kw_status_flag,
/// 5 app_yrc_num (repeated message holding 6 app_saved_no).
/// </para>
/// </summary>
public static class YrcCodec
{
    public const int NameMaxLength = 10;

    /// <summary>Who started a load: the bike (its menu) or the app.</summary>
    public const int StartedByBike = 1;
    public const int StartedByApp = 2;

    /// <summary>Result codes the app answers the bike with when it has nothing to give (the reference app's "saved data" errors).</summary>
    public const int ResultIdNotFound = 513;
    public const int ResultNoDataForMode = 515;

    /// <summary>
    /// Reads a request the bike sent (what it wants listed or loaded). Throws <see cref="FormatException"/> when it is not one.
    /// </summary>
    public static YrcRequest ParseRequest(ReadOnlySpan<byte> data)
    {
        var kinds = new List<int>();
        var modes = new List<int>();
        var saved = new List<int>();
        int modeType = 0, kw = 0;

        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire, out var value, out var payload))
        {
            switch (field)
            {
                case 1:
                    if (wire == 2) kinds.AddRange(ProtoReader.UnpackVarints(payload));
                    else kinds.Add((int)value);
                    break;
                case 2:
                    if (wire == 2) modes.AddRange(ProtoReader.UnpackVarints(payload));
                    else modes.Add((int)value);
                    break;
                case 3: modeType = (int)value; break;
                case 4: kw = (int)value; break;
                case 5:
                    if (wire == 2)
                    {
                        var inner = new ProtoReader(payload);
                        while (inner.Next(out var f, out _, out var v, out _))
                        {
                            if (f == 6) saved.Add((int)v);
                        }
                    }
                    break;
            }
        }
        return new YrcRequest(kinds, modes, modeType, kw, saved);
    }

    /// <summary>
    /// The answer to the bike's request for what is saved on the phone: the entries with their numbers, and the values the bike
    /// asked for. <paramref name="result"/> other than 0 sends only that.
    /// </summary>
    public static byte[] BuildSavedList(IReadOnlyCollection<int> kinds, IEnumerable<YrcProfile> profiles, int result = 0)
    {
        var w = new ProtoWriter();
        if (kinds.Count > 0) w.Packed(1, kinds);
        w.Varint(2, result);
        if (result != 0) return w.ToArray();

        var withCurrent = kinds.Contains(YrcInfoKind.Current);
        var withRange = kinds.Contains(YrcInfoKind.MinMax);
        foreach (var p in profiles)
        {
            var setting = new ProtoWriter()
                .Varint(12, p.Mode)
                .Varint(13, p.SavedNo)
                .Varint(14, p.ModeType)
                .String(15, Truncate(p.Name));
            if (withCurrent || withRange)
            {
                foreach (var v in p.Values)
                {
                    var value = new ProtoWriter().Varint(17, v.Kind);
                    if (withCurrent) value.Varint(18, v.Current);
                    if (withRange) value.Varint(19, v.Min).Varint(20, v.Max);
                    setting.Bytes(16, value.ToArray());
                }
            }
            w.Bytes(11, setting.ToArray());
        }
        return w.ToArray();
    }

    /// <summary>A load message that only carries a result (what the bike gets when it asked for something we do not have).</summary>
    public static byte[] BuildResultOnly(int result, int startTrigger = StartedByBike) =>
        new ProtoWriter().Varint(2, result).Varint(10, startTrigger).ToArray();

    /// <summary>
    /// Builds the request protobuf for the given kinds and modes.
    /// </summary>
    public static byte[] BuildRequest(IReadOnlyCollection<int> kinds, IReadOnlyCollection<int> modes)
    {
        var w = new ProtoWriter().Packed(1, kinds);
        if (modes.Count > 0) w.Packed(2, modes);
        return w.ToArray();
    }

    /// <summary>
    /// Builds the protobuf that makes the bike take over these profiles' names and values.
    /// </summary>
    public static byte[] BuildLoad(IEnumerable<YrcProfile> profiles, int startTrigger = StartedByApp, IReadOnlyCollection<int>? kinds = null)
    {
        // The reference app's loads carry the current values only when the app starts them, and [current, names] (in that order)
        // when answering the bike's import. Names always travel in the setting itself.
        var w = new ProtoWriter().Packed(1, kinds is { Count: > 0 } ? kinds : new[] { YrcInfoKind.Current });
        w.Varint(10, startTrigger);
        foreach (var p in profiles)
        {
            var setting = new ProtoWriter()
                .Varint(12, p.Mode);
            if (p.SavedNo > 0) setting.Varint(13, p.SavedNo); // the reference app leaves it out
            setting.Varint(14, p.ModeType).String(15, Truncate(p.Name.TrimEnd())); // and sends names without the bike's padding
            foreach (var v in p.Values)
                setting.Bytes(16, new ProtoWriter().Varint(17, v.Kind).Varint(18, v.Current).ToArray());
            w.Bytes(11, setting.ToArray());
        }
        return w.ToArray();
    }

    public static string Truncate(string name) => name.Length <= NameMaxLength ? name : name[..NameMaxLength];

    /// <summary>
    /// Parses an info message. Throws <see cref="FormatException"/> when the bytes are not one.
    /// </summary>
    public static YrcInfo ParseInfo(ReadOnlySpan<byte> data)
    {
        var kinds = new List<int>();
        var profiles = new List<YrcProfile>();
        int result = 0, kw = 0, sus = 0, current = 0, currentSus = 0, trigger = 0;
        bool model = false, at = false, track = false;

        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire, out var value, out var payload))
        {
            switch (field)
            {
                case 1:
                    if (wire == 2) kinds.AddRange(ProtoReader.UnpackVarints(payload));
                    else kinds.Add((int)value);
                    break;
                case 2: result = (int)value; break;
                case 3: kw = (int)value; break;
                case 4: model = value != 0; break;
                case 5: at = value != 0; break;
                case 6: track = value != 0; break;
                case 7: sus = (int)value; break;
                case 8: current = (int)value; break;
                case 9: currentSus = (int)value; break;
                case 10: trigger = (int)value; break;
                case 11:
                    if (wire == 2) profiles.Add(ParseProfile(payload));
                    break;
            }
        }
        return new YrcInfo(result, kinds, kw, model, at, track, sus, current, currentSus, trigger, profiles);
    }

    private static YrcProfile ParseProfile(ReadOnlySpan<byte> data)
    {
        int mode = 0, savedNo = 0, type = 0;
        var name = "";
        var values = new List<YrcValue>();

        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire, out var value, out var payload))
        {
            switch (field)
            {
                case 12: mode = (int)value; break;
                case 13: savedNo = (int)value; break;
                case 14: type = (int)value; break;
                case 15: name = Encoding.UTF8.GetString(payload); break;
                case 16:
                    if (wire == 2) values.Add(ParseValue(payload));
                    break;
            }
        }
        return new YrcProfile(mode, savedNo, type, name, values);
    }

    private static YrcValue ParseValue(ReadOnlySpan<byte> data)
    {
        int kind = 0, current = 0, min = 0, max = 0;
        var r = new ProtoReader(data);
        while (r.Next(out var field, out _, out var value, out _))
        {
            switch (field)
            {
                case 17: kind = (int)value; break;
                case 18: current = (int)value; break;
                case 19: min = (int)value; break;
                case 20: max = (int)value; break;
            }
        }
        return new YrcValue(kind, current, min, max);
    }

    /// <summary>
    /// Folds a later response into the profiles collected so far. What a response contains is told by its data kinds
    /// (no kinds listed: everything it carries counts); fields the bike left out are otherwise kept as they were.
    /// </summary>
    public static List<YrcProfile> Merge(IReadOnlyList<YrcProfile> existing, YrcInfo incoming)
    {
        var merged = existing.ToList();
        var all = incoming.DataKinds.Count == 0;
        var hasNames = all || incoming.DataKinds.Contains(YrcInfoKind.Names);
        var hasRange = all || incoming.DataKinds.Contains(YrcInfoKind.MinMax);
        var hasCurrent = all || incoming.DataKinds.Contains(YrcInfoKind.Current);

        foreach (var p in incoming.Profiles)
        {
            var index = merged.FindIndex(m => m.Mode == p.Mode && m.SavedNo == p.SavedNo);
            if (index < 0)
            {
                merged.Add(p);
                continue;
            }

            var old = merged[index];
            var values = old.Values.ToList();
            foreach (var v in p.Values)
            {
                var at = values.FindIndex(x => x.Kind == v.Kind);
                if (at < 0)
                {
                    values.Add(v);
                    continue;
                }
                var o = values[at];
                values[at] = new YrcValue(
                    v.Kind,
                    hasCurrent ? v.Current : o.Current,
                    hasRange ? v.Min : o.Min,
                    hasRange ? v.Max : o.Max);
            }
            merged[index] = old with
            {
                Name = hasNames && p.Name.Length > 0 ? p.Name : old.Name,
                ModeType = p.ModeType != 0 ? p.ModeType : old.ModeType,
                Values = values,
            };
        }
        return merged;
    }
}
