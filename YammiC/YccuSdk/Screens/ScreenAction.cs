namespace CNCO.YccuSdk.Screens;

public enum ScreenActionKind
{
    None,
    NextPage,
    PreviousPage,
    GoTo,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    Toggle,
    Set,
}

/// <summary>
/// What a button press does. Text forms: <c>NextPage</c>, <c>PreviousPage</c>, <c>GoTo:id</c>, <c>Media.PlayPause|Next|Previous</c>, <c>Toggle:flag</c>, <c>Set:name=value</c>, <c>None</c>.
/// </summary>
public readonly record struct ScreenAction(ScreenActionKind Kind, string Arg = "", string Value = "")
{
    public static bool TryParse(string? text, out ScreenAction action)
    {
        action = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var colon = trimmed.IndexOf(':');
        var head = (colon < 0 ? trimmed : trimmed[..colon]).ToLowerInvariant();
        var tail = colon < 0 ? "" : trimmed[(colon + 1)..].Trim();

        switch (head)
        {
            case "none" when colon < 0: action = new(ScreenActionKind.None); return true;
            case "nextpage" when colon < 0: action = new(ScreenActionKind.NextPage); return true;
            case "previouspage" when colon < 0: action = new(ScreenActionKind.PreviousPage); return true;
            case "media.playpause" when colon < 0: action = new(ScreenActionKind.MediaPlayPause); return true;
            case "media.next" when colon < 0: action = new(ScreenActionKind.MediaNext); return true;
            case "media.previous" when colon < 0: action = new(ScreenActionKind.MediaPrevious); return true;
            case "goto" when tail.Length > 0: action = new(ScreenActionKind.GoTo, tail); return true;
            case "toggle" when tail.Length > 0: action = new(ScreenActionKind.Toggle, tail); return true;
            case "set":
                var equals = tail.IndexOf('=');
                if (equals <= 0) return false;
                action = new(ScreenActionKind.Set, tail[..equals].Trim(), tail[(equals + 1)..].Trim());
                return true;
            default:
                return false;
        }
    }
}
