using CNCO.YccuSdk.Bike;
using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets.App;

namespace CNCO.YccuSdk.Tracks;

/// <summary>
/// The numbers of one lap message, in the units and ranges the bike takes.
/// </summary>
public sealed record LapMessage(int LapNo, int LapTimeCs, int ReferenceCs, int[] GapsCs)
{
    /// <summary>
    /// Lap time, the best lap so far as the reference, and per sector the difference to the best lap's sector. The meaning of the
    /// gaps is assumed from the field names and is not yet confirmed on a bike.
    /// </summary>
    public static LapMessage Build(LapCompleted lap, LapCompleted? best)
    {
        var gaps = new int[4];
        for (var i = 0; i < gaps.Length; i++)
        {
            gaps[i] = !lap.Valid ? AppSendLapSectorTime.GapInvalid : best != null && i < lap.SectorTimes.Count && i < best.SectorTimes.Count
                ? Math.Clamp(Centiseconds(lap.SectorTimes[i]) - Centiseconds(best.SectorTimes[i]), -5999, 5999)
                : AppSendLapSectorTime.GapNotMeasured;
        }
        return new LapMessage(
            Math.Min(lap.Lap, 99),
            Math.Min(Centiseconds(lap.Time), 359999),
            best != null ? Math.Min(Centiseconds(best.Time), 359999) : AppSendLapSectorTime.NoTime,
            gaps);
    }

    private static int Centiseconds(TimeSpan t) => (int)(t.TotalMilliseconds / 10);
}

/// <summary>
/// Sends finished laps to the bike with the existing lap-time message.
/// </summary>
public sealed class BikeLapSender : ILapSender
{
    private readonly YmcConnectionBroker _broker;
    private readonly Logger _log;

    public BikeLapSender(YmcConnectionBroker broker, Logger logger)
    {
        _broker = broker;
        _log = logger.Section("Track timer");
    }

    public async Task SendAsync(LapCompleted lap, LapCompleted? best)
    {
        var mb = _broker.MessageBroker;
        if (!_broker.IsBikeConnected || mb == null)
        {
            _log.Info($"Lap {lap.Lap} not sent: bike not connected");
            return;
        }
        var m = LapMessage.Build(lap, best);
        var problem = AppSendLapSectorTime.Validate(m.LapNo, m.LapTimeCs, m.ReferenceCs, 0, m.GapsCs);
        if (problem != null)
        {
            _log.Info($"Lap {lap.Lap} not sent: {problem}");
            return;
        }
        _log.Info($"Lap {m.LapNo}: {m.LapTimeCs} cs (reference {(m.ReferenceCs == AppSendLapSectorTime.NoTime ? "none" : m.ReferenceCs.ToString())})");
        await new AppSendLapSectorTime(mb.PacketHandler, m.LapNo, m.LapTimeCs, m.ReferenceCs, 0, m.GapsCs).SendAsync(mb.ConnectionContext).ConfigureAwait(false);
    }
}

/// <summary>
/// What the Track Timer needs from the bike side. <see cref="BikeServiceTrackLink"/> is the real one; <see cref="NoBikeLink"/> is for platforms without a bike.
/// </summary>
public interface ITrackBikeLink
{
    IReadOnlyList<CanRawReading> RawCan();

    CanValues CanValues();

    /// <summary>
    /// Raised when new CAN data arrived.
    /// </summary>
    event Action? CanUpdated;

    void SetRequiredCanIds(IEnumerable<int> ids);

    Task<bool> ShowBoardMessageAsync(string text);

    Task ReleaseBoardAsync();

    ILapSender LapSender { get; }
}

public sealed class BikeServiceTrackLink : ITrackBikeLink
{
    private readonly BikeService _bike;

    public BikeServiceTrackLink(BikeService bike, Logger logger)
    {
        _bike = bike;
        LapSender = new BikeLapSender(bike.Controller, logger);
    }

    public IReadOnlyList<CanRawReading> RawCan() => _bike.Can.Raw();

    public CanValues CanValues() => _bike.Can.Values(DateTime.UtcNow);

    public event Action? CanUpdated
    {
        add => _bike.Can.Updated += value;
        remove => _bike.Can.Updated -= value;
    }

    public void SetRequiredCanIds(IEnumerable<int> ids) => _bike.SetRequiredCanIds(ids);

    public Task<bool> ShowBoardMessageAsync(string text) => _bike.ShowBoardMessageAsync(text);

    public Task ReleaseBoardAsync() => _bike.ReleaseBoardAsync();

    public ILapSender LapSender { get; }
}

public sealed class NoBikeLink : ITrackBikeLink, ILapSender
{
    public IReadOnlyList<CanRawReading> RawCan() => Array.Empty<CanRawReading>();

    public CanValues CanValues() => Can.CanValues.Empty;

    public event Action? CanUpdated
    {
        add { }
        remove { }
    }

    public void SetRequiredCanIds(IEnumerable<int> ids) { }

    public Task<bool> ShowBoardMessageAsync(string text) => Task.FromResult(false);

    public Task ReleaseBoardAsync() => Task.CompletedTask;

    public ILapSender LapSender => this;

    public Task SendAsync(LapCompleted lap, LapCompleted? best) => Task.CompletedTask;
}

/// <summary>
/// Shows the track name on the board and keeps the custom screen off it while a session is on.
/// </summary>
public sealed class LinkBoardSender : IBoardMessageSender
{
    private readonly ITrackBikeLink _link;

    public LinkBoardSender(ITrackBikeLink link) => _link = link;

    public Task ShowTrackNameAsync(string trackName) => _link.ShowBoardMessageAsync(trackName);

    public Task ReleaseAsync() => _link.ReleaseBoardAsync();
}
