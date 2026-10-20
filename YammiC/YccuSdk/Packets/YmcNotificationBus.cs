using System.Threading.Channels;

namespace CNCO.YccuSdk.Packets;

/// <summary>
/// Broadcasts GATT notifications to any number of listeners (the C# stand-in for a SharedFlow).
/// Every subscriber gets its own unbounded queue, so a slow listener never drops or blocks others.
/// </summary>
public sealed class YmcNotificationBus
{
    private readonly Lock _lock = new();
    private readonly List<Channel<GattNotificationEvent>> _subscribers = new();

    public void Publish(GattNotificationEvent notification)
    {
        lock (_lock)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryWrite(notification);
            }
        }
    }

    /// <summary>
    /// Starts listening immediately; notifications published after this call are queued on the returned reader
    /// until it is disposed.
    /// </summary>
    public Subscription Subscribe()
    {
        var channel = Channel.CreateUnbounded<GattNotificationEvent>(
            new UnboundedChannelOptions { SingleReader = true }
        );
        lock (_lock)
        {
            _subscribers.Add(channel);
        }
        return new Subscription(this, channel);
    }

    /// <summary>
    /// Completes with the first notification matching [predicate]. The subscription is established
    /// before this method returns, so it is safe to call before performing the write that triggers the notification.
    /// </summary>
    public Task<GattNotificationEvent> FirstAsync(
        Func<GattNotificationEvent, bool> predicate,
        CancellationToken cancellationToken = default
    )
    {
        var subscription = Subscribe();
        return Core(subscription);

        async Task<GattNotificationEvent> Core(Subscription sub)
        {
            using (sub)
            {
                await foreach (
                    var notification in sub
                        .Reader.ReadAllAsync(cancellationToken)
                        .ConfigureAwait(false)
                )
                {
                    if (predicate(notification))
                        return notification;
                }
                throw new InvalidOperationException("Notification stream closed.");
            }
        }
    }

    private void Unsubscribe(Channel<GattNotificationEvent> channel)
    {
        lock (_lock)
        {
            _ = _subscribers.Remove(channel);
        }
        _ = channel.Writer.TryComplete();
    }

    public sealed class Subscription : IDisposable
    {
        private readonly YmcNotificationBus _bus;
        private readonly Channel<GattNotificationEvent> _channel;

        internal Subscription(YmcNotificationBus bus, Channel<GattNotificationEvent> channel)
        {
            _bus = bus;
            _channel = channel;
        }

        public ChannelReader<GattNotificationEvent> Reader => _channel.Reader;

        public void Dispose() => _bus.Unsubscribe(_channel);
    }
}
