using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Screens;
using SkiaSharp;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenPresenterRetryTests
{
    private sealed class FakeRenderer : IScreenRenderer
    {
        public string? LastError => null;

        public Task<SKBitmap?> RenderAsync(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height) =>
            Task.FromResult<SKBitmap?>(new SKBitmap(8, 8));
    }

    // A presenter whose picture is different on every check, so it wants to send each time it is allowed to.
    private static List<DateTime> Run(Func<int, bool> sendWorks, TimeSpan minInterval, TimeSpan retryInterval, TimeSpan duration)
    {
        var times = new List<DateTime>();
        var counter = 0;
        var presenter = new ScreenPresenter(
            "test",
            ScreenTarget.VirtualPitBoard,
            new FakeRenderer(),
            () => new ScreenPage { Id = "main", Xaml = "<ScreenCanvas />" },
            () => new ScreenSnapshot(
                new ScreenContext(DateTime.Now, null, false), "main", 0, 1,
                new Dictionary<string, bool>(), new Dictionary<string, string> { ["n"] = (++counter).ToString() }),
            () => (8, 8),
            () => true,
            _ =>
            {
                times.Add(DateTime.UtcNow);
                return Task.FromResult(sendWorks(times.Count));
            },
            tick: () => TimeSpan.FromMilliseconds(20),
            minInterval: minInterval,
            quality: () => 50,
            new Logger("test", Array.Empty<ILogSink>()),
            retryInterval: retryInterval);
        presenter.Start();
        Thread.Sleep(duration);
        presenter.Stop();
        return times;
    }

    [Fact]
    public void AfterASuccessfulSend_TheNextOneWaitsTheMinimumInterval()
    {
        var times = Run(_ => true, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(1000));
        Assert.True(times.Count >= 4, $"sent {times.Count}");
        for (var i = 1; i < times.Count; i++)
        {
            var gap = (times[i] - times[i - 1]).TotalMilliseconds;
            Assert.InRange(gap, 130, 500);
        }
    }

    [Fact]
    public void RetryNow_TriesAgainWithoutWaitingOutTheFailureDelay()
    {
        var times = new List<DateTime>();
        var counter = 0;
        var presenter = new ScreenPresenter(
            "test",
            ScreenTarget.VirtualPitBoard,
            new FakeRenderer(),
            () => new ScreenPage { Id = "main", Xaml = "<ScreenCanvas />" },
            () => new ScreenSnapshot(
                new ScreenContext(DateTime.Now, null, false), "main", 0, 1,
                new Dictionary<string, bool>(), new Dictionary<string, string> { ["n"] = (++counter).ToString() }),
            () => (8, 8),
            () => true,
            _ =>
            {
                times.Add(DateTime.UtcNow);
                return Task.FromResult(times.Count > 1);
            },
            tick: () => TimeSpan.FromMilliseconds(20),
            minInterval: TimeSpan.FromMilliseconds(50),
            quality: () => 50,
            new Logger("test", Array.Empty<ILogSink>()),
            retryInterval: TimeSpan.FromSeconds(30));
        presenter.Start();
        Thread.Sleep(300);
        Assert.Single(times);   // the first send failed; the 30 s retry delay is holding it back
        presenter.RetryNow();
        Thread.Sleep(300);
        presenter.Stop();
        Assert.True(times.Count >= 2, "RetryNow should have sent again at once");
    }

    [Fact]
    public void AfterAFailedSend_TheRetryWaitsTheLongerInterval()
    {
        var times = Run(count => count > 1, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1400));
        Assert.True(times.Count >= 2, $"sent {times.Count}");
        Assert.True((times[1] - times[0]).TotalMilliseconds >= 560, "the retry came too soon after the failure");
    }
}
