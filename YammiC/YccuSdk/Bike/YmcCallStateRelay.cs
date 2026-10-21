using CNCO.YccuSdk.Logging;

namespace CNCO.YccuSdk.Bike;

/// <summary>
/// Forwards phone-call state from an <see cref="IYmcCallStateSource"/> to the CCU
/// (the logic of the Kotlin PhoneCallMonitor, minus the Android telephony plumbing).
/// </summary>
public class YmcCallStateRelay
{
    /// <summary>
    /// The call state callback and the broadcast carrying the incoming number are two separate events and the
    /// callback usually wins the race. When ringing starts without a number we wait this long for the number
    /// before falling back to "Unknown Caller".
    /// </summary>
    private static readonly TimeSpan NumberGracePeriod = TimeSpan.FromMilliseconds(600);

    private readonly IYmcCallStateSource _source;
    private readonly Func<YmcMessageBroker?> _getBroker;
    private readonly Logger _log;
    private readonly object _lock = new();

    private YmcCallState _lastState = YmcCallState.Idle;
    private bool _wasRinging;
    private bool _callActive;
    private bool _ringPushed;
    private string? _incomingPhoneNumber;
    private CancellationTokenSource? _pendingRing;

    public YmcCallStateRelay(IYmcCallStateSource source, Func<YmcMessageBroker?> getBroker, Logger logger)
    {
        _source = source;
        _getBroker = getBroker;
        _log = logger.Section("PhoneCallMonitor");
        _source.CallStateChanged += HandleCallStateChanged;
    }

    public void Start() => _source.Start();

    public void Stop() => _source.Stop();

    /// <summary>
    /// Sends the current call state immediately, e.g. right after connecting.
    /// </summary>
    public async Task PushCurrentCallStateAsync()
    {
        var broker = _getBroker();
        if (broker == null) return;
        bool active;
        lock (_lock) active = _callActive;
        await broker.SendCallChangeAsync(active).ConfigureAwait(false);
    }

    private void HandleCallStateChanged(YmcCallState state, string? incomingNumber)
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(incomingNumber)) _incomingPhoneNumber = incomingNumber;

            switch (state)
            {
                case YmcCallState.OffHook:
                    CancelPendingRing();
                    _ringPushed = false;
                    if (!_callActive)
                    {
                        _callActive = true;
                        _wasRinging = false;
                        Send(broker => broker.SendCallChangeAsync(true));
                    }
                    break;

                case YmcCallState.Ringing:
                    _wasRinging = true;
                    _log.Debug($"RINGING detected: incomingPhoneNumber={_incomingPhoneNumber}");
                    if (_ringPushed && string.IsNullOrEmpty(incomingNumber))
                    {
                        // Second event for the same ring that adds nothing.
                        break;
                    }
                    if (string.IsNullOrEmpty(_incomingPhoneNumber))
                    {
                        ScheduleRingPush();
                    }
                    else
                    {
                        CancelPendingRing();
                        PushRing(_incomingPhoneNumber);
                    }
                    break;

                case YmcCallState.Idle:
                    CancelPendingRing();
                    if (_wasRinging && _lastState == YmcCallState.Ringing)
                    {
                        var number = _incomingPhoneNumber ?? "0";
                        var name = number.Length > 0 ? _source.LookupContactName(number) ?? number : "Unknown Caller";
                        Send(broker => broker.SendIncomingCallInformationAsync(name, number, isMissedCall: false));
                    }
                    _wasRinging = false;
                    _ringPushed = false;
                    _incomingPhoneNumber = null;
                    if (_callActive)
                    {
                        _callActive = false;
                        Send(broker => broker.SendCallChangeAsync(false));
                    }
                    break;
            }
            _lastState = state;
        }
    }

    /// <summary>
    /// Waits briefly for the incoming number, then pushes the ring with whatever we have.
    /// </summary>
    private void ScheduleRingPush()
    {
        CancelPendingRing();
        var cancellation = new CancellationTokenSource();
        _pendingRing = cancellation;
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(NumberGracePeriod, token).ConfigureAwait(false);
                lock (_lock)
                {
                    if (token.IsCancellationRequested) return;
                    var number = _incomingPhoneNumber ?? "";

                    // Fallback: the phone's own line number (only helps if the platform can't tell us the caller).
                    if (number.Length == 0 && !string.IsNullOrEmpty(_source.LineNumber))
                    {
                        number = _source.LineNumber!;
                        _log.Debug($"Fallback to line number: {number}");
                    }
                    PushRing(number);
                }
            }
            catch (OperationCanceledException)
            {
                // The number arrived (or the ring ended) first.
            }
        });
    }

    private void CancelPendingRing()
    {
        _pendingRing?.Cancel();
        _pendingRing = null;
    }

    // Caller holds _lock.
    private void PushRing(string number)
    {
        _ringPushed = true;
        var name = number.Length > 0 ? _source.LookupContactName(number) ?? number : "Unknown Caller";
        _log.Debug($"Sending incoming call: name={name} number={number}");
        Send(broker => broker.SendIncomingCallInformationAsync(name, number, isMissedCall: false));
    }

    private void Send(Func<YmcMessageBroker, Task> send)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var broker = _getBroker();
                if (broker == null) return;
                await send(broker).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _log.Debug($"send failed: {e.Message}");
            }
        });
    }
}
