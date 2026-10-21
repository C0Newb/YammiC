using System.Xml;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

public sealed class ScreenDocumentException : Exception
{
    public ScreenDocumentException(int line, string message) : base(line > 0 ? $"line {line}: {message}" : message)
    {
        Line = line;
    }

    public int Line { get; }
}

/// <summary>
/// One screen (navigation or pit board): a list of pages, each the XAML of a root control, plus what the bike's buttons do.
/// Stored and exchanged as one <c>.axaml</c> text (see <see cref="Parse"/>/<see cref="ToAxaml"/>).
/// </summary>
public sealed class ScreenDocument
{
    public const string AvaloniaNs = "https://github.com/avaloniaui";
    public const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string WidgetNs = "clr-namespace:CNCO.YammiC.Screens;assembly=YammiC";

    public ScreenTarget Target { get; set; }
    public List<ScreenPage> Pages { get; } = new();

    /// <summary>
    /// Used for an event the current page has no action for.
    /// </summary>
    public Dictionary<ScreenEvent, string> DefaultActions { get; } = new();

    /// <summary>
    /// The road name shown on the bike (a template with {variables}) for a page that has none of its own; null shows nothing.
    /// </summary>
    public string? DefaultRoadName { get; set; }

    public ScreenPage? Find(string id) => Pages.FirstOrDefault(p => p.Id == id);

    public ScreenDocument Clone()
    {
        var copy = new ScreenDocument { Target = Target, DefaultRoadName = DefaultRoadName };
        foreach (var page in Pages) copy.Pages.Add(page.Clone());
        foreach (var action in DefaultActions) copy.DefaultActions[action.Key] = action.Value;
        return copy;
    }

    public static ScreenDocument Parse(string axaml)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Parse(axaml, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            throw new ScreenDocumentException(e.LineNumber, e.Message);
        }

        var root = xml.Root!;
        if (root.Name.LocalName != "ScreenDocument") throw new ScreenDocumentException(Line(root), "The root element must be <y:ScreenDocument>.");

        var declarations = root.Attributes().Where(a => a.IsNamespaceDeclaration).ToList();
        var document = new ScreenDocument
        {
            Target = Enum.TryParse<ScreenTarget>(root.Attribute("Target")?.Value, out var target) ? target : ScreenTarget.Navigation,
            DefaultRoadName = root.Attribute("RoadName")?.Value,
        };
        ReadActions(root, document.DefaultActions);

        foreach (var page in root.Elements())
        {
            if (page.Name.LocalName != "Page") throw new ScreenDocumentException(Line(page), $"Unexpected <{page.Name.LocalName}>; a screen holds only <y:Page> elements.");

            var id = page.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) throw new ScreenDocumentException(Line(page), "A page needs an Id.");
            if (document.Find(id) != null) throw new ScreenDocumentException(Line(page), $"There are two pages with the Id \"{id}\".");

            var content = page.Elements().ToList();
            if (content.Count != 1) throw new ScreenDocumentException(Line(page), $"Page \"{id}\" must contain exactly one root element.");

            // Make the page stand alone: it carries the document's xmlns declarations.
            var copy = new XElement(content[0]);
            foreach (var declaration in declarations)
                if (copy.Attribute(declaration.Name) == null) copy.Add(new XAttribute(declaration.Name, declaration.Value));

            var result = new ScreenPage
            {
                Id = id,
                Name = page.Attribute("Name")?.Value ?? id,
                Xaml = copy.ToString(),
                RoadName = page.Attribute("RoadName")?.Value,
            };
            ReadActions(page, result.Actions);
            document.Pages.Add(result);
        }

        if (document.Pages.Count == 0) throw new ScreenDocumentException(Line(root), "A screen needs at least one page.");
        return document;
    }

    public string ToAxaml()
    {
        XNamespace widgets = WidgetNs;
        var root = new XElement(widgets + "ScreenDocument",
            new XAttribute("xmlns", AvaloniaNs),
            new XAttribute(XNamespace.Xmlns + "x", XamlNs),
            new XAttribute(XNamespace.Xmlns + "y", WidgetNs),
            new XAttribute("Target", Target.ToString()));
        if (DefaultRoadName != null) root.SetAttributeValue("RoadName", DefaultRoadName);
        WriteActions(root, DefaultActions);

        var shared = new Dictionary<XName, string>
        {
            [XNamespace.None + "xmlns"] = AvaloniaNs,
            [XNamespace.Xmlns + "x"] = XamlNs,
            [XNamespace.Xmlns + "y"] = WidgetNs,
        };

        foreach (var page in Pages)
        {
            var element = new XElement(widgets + "Page", new XAttribute("Id", page.Id), new XAttribute("Name", page.Name));
            if (page.RoadName != null) element.SetAttributeValue("RoadName", page.RoadName);
            WriteActions(element, page.Actions);

            var content = XElement.Parse(page.Xaml);
            content.Attributes().Where(a => a.IsNamespaceDeclaration && shared.TryGetValue(a.Name, out var value) && value == a.Value).Remove();
            element.Add(content);
            root.Add(element);
        }

        return root.ToString();
    }

    private static void ReadActions(XElement element, Dictionary<ScreenEvent, string> into)
    {
        foreach (var screenEvent in Enum.GetValues<ScreenEvent>())
        {
            var value = element.Attribute(screenEvent.ToString())?.Value;
            if (!string.IsNullOrWhiteSpace(value)) into[screenEvent] = value.Trim();
        }
    }

    private static void WriteActions(XElement element, Dictionary<ScreenEvent, string> actions)
    {
        foreach (var action in actions.OrderBy(a => a.Key)) element.SetAttributeValue(action.Key.ToString(), action.Value);
    }

    private static int Line(XObject node) => (node as IXmlLineInfo)?.LineNumber ?? 0;
}
