using System.Text.RegularExpressions;

namespace CNCO.YccuSdk.Screens;

/// <summary>What a button press on the bike can raise on a page.</summary>
public enum ScreenEvent
{
    ZoomIn,
    ZoomOut,
}

/// <summary>One page of a screen: an id, a display name, the XAML of its root control and what its events do.</summary>
public sealed partial class ScreenPage
{
    private static readonly Regex SecondsPattern = SecondsRegex();
    private static readonly Regex CanPattern = new(
        @"\b(" + string.Join('|', ScreenVariables.CanVariableNames) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The root control as XAML, with its xmlns declarations.</summary>
    public string Xaml { get; set; } = "";

    /// <summary>Event name → action text (see <see cref="ScreenAction"/>). Missing = use the document's default.</summary>
    public Dictionary<ScreenEvent, string> Actions { get; } = new();

    /// <summary>True when the page shows seconds (so it must redraw every second, not every minute).</summary>
    public bool UsesSeconds => SecondsPattern.IsMatch(Xaml);

    /// <summary>True when the page mentions bike data (it then redraws faster). May be true for a page that merely contains the word.</summary>
    public bool UsesCan => CanPattern.IsMatch(Xaml);

    public ScreenPage Clone()
    {
        var copy = new ScreenPage { Id = Id, Name = Name, Xaml = Xaml };
        foreach (var action in Actions) copy.Actions[action.Key] = action.Value;
        return copy;
    }

    // "ss" as a whole word (a format such as HH:mm:ss) or :s, or Now.Second; not the "ss" inside "Progress".
    [GeneratedRegex(@"(?<![A-Za-z])ss(?![A-Za-z])|:s(?![A-Za-z])|\.Second", RegexOptions.None)]
    private static partial Regex SecondsRegex();
}
