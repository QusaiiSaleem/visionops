using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VisionOps.Service.Events;

/// <summary>
/// Simple in-process event bus for component communication
/// </summary>
public interface IEventBus
{
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    Task PublishAsync<TEvent>(TEvent eventData) where TEvent : class;
}

public sealed class EventBus : IEventBus, IDisposable
{
    private readonly ILogger<EventBus> _logger;
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        _lock.Wait();
        try
        {
            var eventType = typeof(TEvent);
            if (!_handlers.ContainsKey(eventType))
            {
                _handlers[eventType] = new List<Delegate>();
            }

            _handlers[eventType].Add(handler);
            _logger.LogDebug("Subscribed handler for event type {EventType}", eventType.Name);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        _lock.Wait();
        try
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers.Remove(handler);
                _logger.LogDebug("Unsubscribed handler for event type {EventType}", eventType.Name);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : class
    {
        if (eventData == null) return;

        await _lock.WaitAsync();
        List<Delegate>? handlers = null;

        try
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var registeredHandlers))
            {
                handlers = new List<Delegate>(registeredHandlers);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (handlers != null && handlers.Any())
        {
            _logger.LogDebug("Publishing event {EventType} to {HandlerCount} handlers",
                typeof(TEvent).Name, handlers.Count);

            // Execute handlers in parallel for better performance
            var tasks = handlers.Select(handler => Task.Run(() =>
            {
                try
                {
                    ((Action<TEvent>)handler)(eventData);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing handler for event {EventType}",
                        typeof(TEvent).Name);
                }
            }));

            await Task.WhenAll(tasks);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _handlers.Clear();
        _lock?.Dispose();
        _disposed = true;
    }
}