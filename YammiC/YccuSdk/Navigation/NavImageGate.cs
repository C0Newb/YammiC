using CNCO.YccuSdk.Imaging;
using CNCO.YccuSdk.Navigation.Protocol;
using CNCO.YccuSdk.Screens;

namespace CNCO.YccuSdk.Navigation;

/// <summary>Whether the bike is showing the navigation picture and wants to be sent one.</summary>
public enum NavImageDemand
{
    /// <summary>Nothing heard yet on this connection (the bike hasn't said whether it is on the navigation screen).</summary>
    Unknown,

    /// <summary>The rider opened the navigation screen (START_CONTENT_UPDATE_REQUEST for the picture).</summary>
    Wanted,

    /// <summary>The rider left it (STOP_CONTENT_UPDATE_REQUEST).</summary>
    Paused,
}

/// <summary>
/// Tracks the bike's START/STOP content requests for the navigation picture, so the single place that sends pictures can hold
/// them back while nobody is looking. Only the picture counts (the favourites and stations lists are other content types).
/// </summary>
public sealed class NavImageGate
{
    private readonly object _gate = new();
    private NavImageDemand _demand = NavImageDemand.Unknown;

    /// <summary>Raised with the new state each time it really changes.</summary>
    public event Action<NavImageDemand>? Changed;

    public NavImageDemand Demand
    {
        get
        {
            lock (_gate) return _demand;
        }
    }

    /// <summary>True only while the bike has asked for pictures and not stopped.</summary>
    public bool ImagesWanted => Demand == NavImageDemand.Wanted;

    /// <summary>The bike opened (<paramref name="started"/>) or left a content screen.</summary>
    public void OnContentRequest(int contentType, bool started)
    {
        if (contentType != NaviLiteContentType.NaviImage) return;
        Set(started ? NavImageDemand.Wanted : NavImageDemand.Paused);
    }

    /// <summary>A new connection: nothing is known again.</summary>
    public void Reset() => Set(NavImageDemand.Unknown);

    /// <summary>The line shown on the bike when pictures are held back, or null when they are wanted.</summary>
    public static string? StatusText(NavImageDemand demand) => demand switch
    {
        NavImageDemand.Paused => "Rendering paused.",
        NavImageDemand.Unknown => "Rendering paused (unknown state)",
        _ => null,
    };

    private void Set(NavImageDemand demand)
    {
        lock (_gate)
        {
            if (_demand == demand) return;
            _demand = demand;
        }
        Changed?.Invoke(demand);
    }
}

/// <summary>A plain one-line picture for the navigation display (used for the "rendering paused" notices).</summary>
public static class NavigationStatusImage
{
    public static byte[] Render(string text, NavDisplaySize size)
    {
        var layout = new ScreenLayout
        {
            Target = ScreenTarget.Navigation,
            Background = "#000000",
            Elements =
            {
                new ScreenElement
                {
                    Kind = ScreenElementKind.Text,
                    Text = text.Replace("{", "{{").Replace("}", "}}"),
                    X = 20, Y = 80, Width = 440, Height = 80,
                    FontSize = 28, Bold = true, Align = ScreenTextAlign.Center, Color = "#FFFFFF",
                },
            },
        };
        using var bitmap = ScreenRenderer.Render(layout, new ScreenContext(DateTime.Now, null, true), size.Width, size.Height);
        return YmcImaging.EncodeJpeg(bitmap, 85);
    }
}
