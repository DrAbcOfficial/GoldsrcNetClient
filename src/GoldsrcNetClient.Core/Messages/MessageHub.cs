namespace GoldsrcNetClient.Core.Messages;

/// <summary>
/// Typed publish/subscribe hub for parsed server messages. Consumers subscribe
/// by message type; the pipeline publishes each parsed message exactly once.
/// Dispatch matches the message's <em>exact</em> runtime type.
/// </summary>
/// <remarks>
/// <para>
/// This replaces per-message C# events: a game profile or application registers
/// only the messages it cares about, and third parties can publish/subscribe
/// their own <see cref="IServerMessage"/> records without touching the library.
/// </para>
/// <para>
/// Subscriptions are expected to be set up while not connected; publish takes a
/// lock-free snapshot, so subscribing during traffic is safe but takes a copy.
/// Handlers run synchronously on the parsing thread — keep them fast and never
/// block. An exception in one handler is contained and does not affect others.
/// </para>
/// <code>
/// using var sub = hub.Subscribe&lt;PrintMessage&gt;(m => Console.WriteLine(m.Text));
/// using var chat = hub.Subscribe&lt;SayTextMessage&gt;(m => ShowChat(m.Text));
/// </code>
/// </remarks>
public sealed class MessageHub
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly Lock _lock = new();
    private Dictionary<Type, Delegate[]> _snapshot = [];

    /// <summary>Subscribes a handler for one message type. Dispose the returned
    /// registration to unsubscribe.</summary>
    public IDisposable Subscribe<T>(Action<T> handler) where T : class, IServerMessage
    {
        ArgumentNullException.ThrowIfNull(handler);

        // Wrap into a common delegate type — Action<T> is not contravariantly
        // convertible to Action<IServerMessage>, but the cast inside is exact
        // because dispatch matches the message's runtime type.
        Action<IServerMessage> wrapped = message => handler((T)message);

        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
                _handlers[typeof(T)] = list = [];
            list.Add(wrapped);
            _snapshot = []; // invalidate; rebuilt lazily on next publish
        }

        return new Subscription(this, typeof(T), wrapped);
    }

    /// <summary>Publishes a message to every handler subscribed for its exact runtime type.</summary>
    public void Publish(IServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var handlers = _snapshot;
        if (handlers.Count == 0)
        {
            lock (_lock)
            {
                handlers = _snapshot = _handlers.ToDictionary(
                    kv => kv.Key, kv => kv.Value.ToArray());
            }
        }

        if (!handlers.TryGetValue(message.GetType(), out var actions))
            return;

        foreach (var action in actions)
        {
            try
            {
                ((Action<IServerMessage>)action)(message);
            }
            catch (Exception)
            {
                // A misbehaving subscriber must not break the receive loop.
            }
        }
    }

    private void Unsubscribe(Type type, Delegate handler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(type, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0)
                    _handlers.Remove(type);
            }
            _snapshot = [];
        }
    }

    private sealed class Subscription(MessageHub hub, Type type, Delegate handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                hub.Unsubscribe(type, handler);
        }
    }
}
