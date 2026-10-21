using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

public sealed record ScreenIssue(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"line {Line}: {Message}" : Message;
}

/// <summary>
/// Checks a page's XAML against the allowed controls, properties and bindings <i>before</i> it reaches the XAML loader (which
/// would happily instantiate anything). Only attributes are allowed (no property elements), and markup extensions are limited
/// to a plain <c>{Binding ...}</c> without converters or other sources.
/// </summary>
public static partial class ScreenXamlValidator
{
    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "Canvas", "Grid", "StackPanel", "Border", "TextBlock", "Rectangle", "Ellipse",
        "ScreenCanvas", "ScreenBox", "VarText", "ScreenImage", "MediaProgress",
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "Canvas.Left", "Canvas.Top", "Canvas.ZIndex", "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding", "Opacity", "IsVisible", "ClipToBounds", "Tag",
        "HorizontalAlignment", "VerticalAlignment", "Background", "NightBackground", "Foreground", "NightForeground",
        "Fill", "Track", "NightFill", "NightTrack", "Stroke", "StrokeThickness", "CornerRadius", "BorderBrush", "BorderThickness",
        "FontSize", "FontWeight", "FontStyle", "FontFamily", "TextAlignment", "TextWrapping", "TextTrimming", "MaxLines", "LineHeight",
        "Text", "Template", "Data", "Origin", "Stretch", "Value", "Orientation", "Spacing", "RowDefinitions", "ColumnDefinitions",
    };

    private static readonly string[] BannedInBinding = { "Converter", "Source", "ElementName", "RelativeSource", "x:", "Static", "Resource" };

    public static IReadOnlyList<ScreenIssue> Validate(string xaml)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xaml, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            return new[] { new ScreenIssue(e.LineNumber, e.Message) };
        }

        var issues = new List<ScreenIssue>();
        Visit(root, issues);
        return issues;
    }

    private static void Visit(XElement element, List<ScreenIssue> issues)
    {
        var line = (element as IXmlLineInfo)?.LineNumber ?? 0;
        var name = element.Name.LocalName;

        var knownNamespace = element.Name.NamespaceName is ScreenDocument.AvaloniaNs or ScreenDocument.WidgetNs;
        if (name.Contains('.')) issues.Add(new ScreenIssue(line, $"<{name}> isn't supported; set the property as an attribute instead."));
        else if (!knownNamespace || !Elements.Contains(name)) issues.Add(new ScreenIssue(line, $"<{name}> isn't an allowed element."));

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;

            var attributeName = attribute.Name.LocalName;
            var allowed = Attributes.Contains(attributeName) && attribute.Name.NamespaceName.Length == 0
                          || attribute.Name.NamespaceName == ScreenDocument.XamlNs && attributeName == "Name";
            if (!allowed)
            {
                issues.Add(new ScreenIssue(line, $"\"{attributeName}\" isn't an allowed property on <{name}>."));
                continue;
            }

            var problem = CheckValue(attribute.Value);
            if (problem != null) issues.Add(new ScreenIssue(line, $"{attributeName}: {problem}"));
        }

        foreach (var child in element.Elements()) Visit(child, issues);
    }

    private static string? CheckValue(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith('{') || text.StartsWith("{}", StringComparison.Ordinal)) return null; // plain text (or an escaped literal)

        if (!text.StartsWith("{Binding", StringComparison.Ordinal) || (text.Length > 8 && !char.IsWhiteSpace(text[8]) && text[8] != '}' && text[8] != ','))
            return "only {Binding ...} is allowed as a markup extension.";
        foreach (var banned in BannedInBinding)
            if (text.Contains(banned, StringComparison.Ordinal)) return $"a binding can't use \"{banned}\".";

        // What is left after removing the escaped StringFormat pieces ({}{0:...}) must be exactly one { ... }.
        var stripped = EscapedFormat().Replace(text, "");
        if (stripped.Count(c => c == '{') != 1 || stripped.Count(c => c == '}') != 1 || !stripped.EndsWith('}'))
            return "the binding is malformed.";
        return null;
    }

    [GeneratedRegex(@"\{\}\{[^{}]*\}")]
    private static partial Regex EscapedFormat();
}
