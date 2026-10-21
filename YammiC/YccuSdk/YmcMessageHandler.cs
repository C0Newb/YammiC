using CNCO.YccuSdk.Handlers;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk;

/// <summary>
/// Processes and handles incoming packets from the CCU.
/// Forwards deserialize requests and responses to their respective handlers.
/// </summary>
public class YmcMessageHandler
{
    private readonly Logger _logger;
    private readonly Logger _log;
    private readonly IYmcPacketHandler _packetHandler;
    private readonly YmcNotificationBus _notifications;

    /// <summary>
    /// Message handler task
    /// </summary>
    private CancellationTokenSource? _messageHandlerCancellation;
    private YmcConnectionContext? _connectionContext;

    // Handlers are async but run sequentially inside the receive loop.
    // This prevents concurrent handlers from interleaving their BLE writes
    // (e.g. a datetime response being injected mid-weather sequence).
    private readonly Dictionary<int, Dictionary<object, Func<byte[], Task>>> _handlers = new();
    private readonly object _handlersLock = new();

    public YmcMessageHandler(Logger logger, IYmcPacketHandler packetHandler, YmcNotificationBus notifications)
    {
        _logger = logger;
        _log = logger.Section("MessageHandler");
        _packetHandler = packetHandler;
        _notifications = notifications;
    }

    /// <summary>
    /// Registers a handler for a specific command (request or response <see cref="YmcCommandCode"/>).
    /// </summary>
    /// <param name="commandCode">Command ID to register for.</param>
    /// <param name="factory">Builds the command (request or response) from the raw bytes, such as <c>(ph, d) => new CcuPingRequest(ph, d)</c>.</param>
    /// <param name="handler">Function that should handle (process) the command (request or response instance).</param>
    public void Register<T>(YmcCommandCode commandCode, Func<IYmcPacketHandler, byte[], T> factory, Action<T> handler)
        where T : CcuIncomingPacket
    {
        Func<byte[], Task> wrapped = bytes =>
        {
            handler(factory(_packetHandler, bytes));
            return Task.CompletedTask;
        };
        AddHandler(commandCode, handler, wrapped);
    }

    /// <summary>
    /// Registers a <see cref="CcuRequestHandler{T}"/> for a specific command <see cref="YmcCommandCode"/>.
    /// </summary>
    /// <param name="commandCode">Command ID to register for.</param>
    /// <param name="factory">Builds the command from the raw bytes, such as <c>(ph, d) => new CcuGetDeviceLanguageRequest(ph, d)</c>.</param>
    /// <param name="handler"><see cref="CcuRequestHandler{T}"/> instance to handle the command.</param>
    public void RegisterHandler<T>(YmcCommandCode commandCode, Func<IYmcPacketHandler, byte[], T> factory, CcuRequestHandler<T> handler)
        where T : CcuIncomingPacket
    {
        Func<byte[], Task> wrapped = bytes =>
        {
            var ctx = _connectionContext;
            if (ctx == null) return Task.CompletedTask;
            return handler.InvokeAsync(factory(_packetHandler, bytes), _packetHandler, ctx);
        };
        AddHandler(commandCode, handler, wrapped);
    }

    /// <summary>
    /// Removes a specific handler for a specific command (request or response <see cref="YmcCommandCode"/>).
    /// </summary>
    /// <param name="commandCode">Command ID to remove a handler for.</param>
    /// <param name="handler">Function to remove as a handler for <paramref name="commandCode"/>.</param>
    public void Unregister<T>(YmcCommandCode commandCode, Action<T> handler)
        where T : CcuIncomingPacket
    {
        lock (_handlersLock)
        {
            if (_handlers.TryGetValue((int)commandCode, out var forCommand)) forCommand.Remove(handler);
        }
    }

    private void AddHandler(YmcCommandCode commandCode, object key, Func<byte[], Task> wrapped)
    {
        lock (_handlersLock)
        {
            if (!_handlers.TryGetValue((int)commandCode, out var forCommand))
            {
                forCommand = new Dictionary<object, Func<byte[], Task>>();
                _handlers[(int)commandCode] = forCommand;
            }
            forCommand[key] = wrapped;
        }
    }

    /// <summary>
    /// Starts the message handler.
    /// </summary>
    /// <param name="connectionContext">Connection context for the active BLE session.</param>
    public void StartMessageHandler(YmcConnectionContext connectionContext)
    {
        _connectionContext = connectionContext;
        _messageHandlerCancellation?.Cancel();

        var cancellation = new CancellationTokenSource();
        _messageHandlerCancellation = cancellation;
        var subscription = _notifications.Subscribe();
        _ = Task.Run(() => ReceiveLoopAsync(subscription, cancellation.Token));
    }

    /// <summary>
    /// Stops the message handler.
    /// </summary>
    public void StopMessageHandler()
    {
        _messageHandlerCancellation?.Cancel();
        _messageHandlerCancellation = null;
        _connectionContext = null;
    }

    private async Task ReceiveLoopAsync(YmcNotificationBus.Subscription subscription, CancellationToken cancellationToken)
    {
        using (subscription)
        {
            try
            {
                await foreach (var notification in subscription.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (notification.Uuid != YmcBikeProtocol.MainRxUuid || notification.Value.Length < 2) continue;
                    var bytes = notification.Value;
                    var commandCode = (bytes[0] << 8) | bytes[1];

                    Func<byte[], Task>[] snapshot;
                    lock (_handlersLock)
                    {
                        snapshot = _handlers.TryGetValue(commandCode, out var forCommand)
                            ? forCommand.Values.ToArray()
                            : Array.Empty<Func<byte[], Task>>();
                    }

                    foreach (var handler in snapshot)
                    {
                        try
                        {
                            await handler(bytes).ConfigureAwait(false);
                        }
                        catch (MalformedPacketException e)
                        {
                            _log.Debug($"malformed packet cmd=0x{commandCode:x}: {e.Message}");
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            _log.Debug($"handler error cmd=0x{commandCode:x} {e.GetType().Name}: {e.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
            catch (Exception e)
            {
                _logger.Error($"{nameof(YmcMessageHandler)}: {e.GetType().FullName}: {e.Message}\n{e.StackTrace}", e);
            }
        }
    }
}
