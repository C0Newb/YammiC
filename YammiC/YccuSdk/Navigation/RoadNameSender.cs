namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// Sends the road name to the bike when it changes (and only then), once the bike can take it.
/// </summary>
public sealed class RoadNameSender
{
    private readonly object _gate = new();
    private readonly Func<bool> _canSend;
    private readonly Func<string, bool> _send;
    private string? _last;

    /// <param name="canSend">False while the navigation link isn't up: nothing is sent and nothing is remembered.</param>
    /// <param name="send">Hands the text to the bike; returns false if it could not (it is then tried again on the next update).</param>
    public RoadNameSender(Func<bool> canSend, Func<string, bool> send)
    {
        _canSend = canSend;
        _send = send;
    }

    public void Update(string text)
    {
        lock (_gate)
        {
            if (!_canSend() || text == _last) return;
            if (_send(text)) _last = text;
        }
    }

    /// <summary>
    /// Forgets what was sent, so the next <see cref="Update"/> sends again (e.g. a new connection).
    /// </summary>
    public void Reset()
    {
        lock (_gate) _last = null;
    }
}
