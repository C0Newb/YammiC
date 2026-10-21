using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Tracks;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class PitOutTests
{
    private static List<TrackEvent> Run(Track track) =>
        TrackTestData.Lap(0).SelectMany(new LapDetector(track).Process).ToList();

    private static List<TrackEvent> Run(LapDetector detector) =>
        TrackTestData.Lap(0).SelectMany(detector.Process).ToList();

    [Fact]
    public void CrossingThePitOutLine_MarksTheLapInProgressInvalid()
    {
        var track = TrackTestData.Circuit();
        track.PitOut = TrackTestData.VerticalLine(50);

        var events = Run(track);

        Assert.Contains(events, e => e is PitOutCrossed { Lap: 1 });
        Assert.False(events.OfType<LapCompleted>().Single().Valid);
    }

    [Fact]
    public void WithoutCrossingIt_TheLapStaysValid()
    {
        var track = TrackTestData.Circuit();
        track.PitOut = TrackTestData.VerticalLine(-50);

        var events = Run(track);

        Assert.DoesNotContain(events, e => e is PitOutCrossed);
        Assert.True(events.OfType<LapCompleted>().Single().Valid);
    }

    [Fact]
    public void ALapWithoutAPitOutLine_IsValid()
    {
        Assert.True(Run(TrackTestData.Circuit()).OfType<LapCompleted>().Single().Valid);
    }

    [Fact]
    public void TheInvalidMarkIsClearedForTheNextLap()
    {
        var track = TrackTestData.Circuit(0);
        track.PitOut = TrackTestData.VerticalLine(50);
        var detector = new LapDetector(track);

        // Lap 1 passes the pit-out line; lap 2 only loops around the start line and never reaches it.
        var first = Run(detector).OfType<LapCompleted>().Single();
        Assert.False(first.Valid);
        var rest = new[] { TrackTestData.Fix(25, -5), TrackTestData.Fix(26, 5) }
            .SelectMany(detector.Process).OfType<LapCompleted>().Single();
        Assert.True(rest.Valid);
    }

    [Fact]
    public void AnInvalidLap_IsSentToTheBikeWithInvalidGaps()
    {
        var lap = new LapCompleted(1, TimeSpan.FromSeconds(60), new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) }, DateTime.UtcNow, false);
        var message = LapMessage.Build(lap, null);
        Assert.All(message.GapsCs, g => Assert.Equal(AppSendLapSectorTime.GapInvalid, g));
    }

    [Fact]
    public void ThePitOutLineTravelsWithTheTrackFile()
    {
        var track = TrackTestData.Circuit();
        track.PitOut = TrackTestData.VerticalLine(50);
        var back = Track.FromJson(track.ToJson());
        Assert.NotNull(back?.PitOut);
        Assert.Equal(track.PitOut, back!.PitOut);
    }
}

public class DirectedLineTests
{
    // Only eastward driving (start line, then the sector line), so each test sees one direction.
    private static List<TrackEvent> Run(Track track) =>
        new[] { TrackTestData.Fix(0, -5), TrackTestData.Fix(1, 5), TrackTestData.Fix(9, 95), TrackTestData.Fix(10, 105) }
            .SelectMany(new LapDetector(track).Process).ToList();

    [Fact]
    public void ADirectedSectorLine_CountsOnlyInItsDirection()
    {
        // The test lap drives east; A→B pointing north has east on its right, so that direction counts. Flipped, it must not.
        var track = TrackTestData.Circuit();
        track.Sectors[0] = track.Sectors[0] with { Directed = true };
        Assert.Contains(Run(track), e => e is SectorCompleted);

        track.Sectors[0] = track.Sectors[0].Flipped();
        Assert.DoesNotContain(Run(track), e => e is SectorCompleted);
    }

    [Fact]
    public void ADirectedStartFinish_IgnoresTheWrongWay_FromTheFirstCrossing()
    {
        var track = TrackTestData.Circuit(0);
        track.StartFinish = track.StartFinish! with { Directed = true };
        Assert.Contains(Run(track), e => e is LapStarted);

        track.StartFinish = track.StartFinish.Flipped();
        Assert.DoesNotContain(Run(track), e => e is LapStarted);
    }

    [Fact]
    public void Direction_SurvivesTheTrackFile()
    {
        var track = TrackTestData.Circuit();
        track.StartFinish = track.StartFinish! with { Directed = true };
        Assert.True(Track.FromJson(track.ToJson())!.StartFinish!.Directed);
    }
}
