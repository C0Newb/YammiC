using CNCO.YccuSdk.Navigation;
using SkiaSharp;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class NavImageGateTests
{
    private const int NaviImage = 1;
    private const int Favorites = 3;

    [Fact]
    public void StartsUnknown_AndImagesAreNotWanted()
    {
        var gate = new NavImageGate();
        Assert.Equal(NavImageDemand.Unknown, gate.Demand);
        Assert.False(gate.ImagesWanted);
    }

    [Fact]
    public void StartThenStop_OfTheNavImage_OpensAndClosesTheGate()
    {
        var gate = new NavImageGate();
        gate.OnContentRequest(NaviImage, started: true);
        Assert.Equal(NavImageDemand.Wanted, gate.Demand);
        Assert.True(gate.ImagesWanted);

        gate.OnContentRequest(NaviImage, started: false);
        Assert.Equal(NavImageDemand.Paused, gate.Demand);
        Assert.False(gate.ImagesWanted);
    }

    [Fact]
    public void OtherContentTypes_AreIgnored()
    {
        var gate = new NavImageGate();
        gate.OnContentRequest(Favorites, started: true);
        Assert.Equal(NavImageDemand.Unknown, gate.Demand);

        gate.OnContentRequest(NaviImage, started: true);
        gate.OnContentRequest(Favorites, started: false);
        Assert.Equal(NavImageDemand.Wanted, gate.Demand);
    }

    [Fact]
    public void Changed_FiresOncePerRealChange()
    {
        var gate = new NavImageGate();
        var seen = new List<NavImageDemand>();
        gate.Changed += seen.Add;

        gate.OnContentRequest(NaviImage, started: true);
        gate.OnContentRequest(NaviImage, started: true);   // repeat: not a change
        gate.OnContentRequest(NaviImage, started: false);
        gate.OnContentRequest(NaviImage, started: false);
        gate.Reset();
        gate.Reset();

        Assert.Equal(new[] { NavImageDemand.Wanted, NavImageDemand.Paused, NavImageDemand.Unknown }, seen);
    }

    [Fact]
    public void StatusText_NamesTheState()
    {
        Assert.Equal("Rendering paused.", NavImageGate.StatusText(NavImageDemand.Paused));
        Assert.Equal("Rendering paused (unknown state)", NavImageGate.StatusText(NavImageDemand.Unknown));
        Assert.Null(NavImageGate.StatusText(NavImageDemand.Wanted));
    }

    [Fact]
    public void StatusImage_IsAJpegAtTheDisplaySize_AndDiffersByText()
    {
        var size = new NavDisplaySize(480, 234);
        var paused = NavigationStatusImage.Render("Rendering paused.", size);
        var unknown = NavigationStatusImage.Render("Rendering paused (unknown state)", size);

        using var decoded = SKBitmap.Decode(paused);
        Assert.NotNull(decoded);
        Assert.Equal((480, 234), (decoded!.Width, decoded.Height));
        Assert.Equal(0xFF, paused[0]);   // JPEG start of image
        Assert.Equal(0xD8, paused[1]);
        Assert.NotEqual(paused, unknown);
    }
}
