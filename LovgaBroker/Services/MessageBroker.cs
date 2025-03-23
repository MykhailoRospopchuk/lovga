namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Models;

public class MessageBroker : IMessageBroker
{
    private ConcurrentDictionary<string, ConcurrentQueue<Message>> _queues = new ();
    private ConcurrentDictionary<string, List<Func<Message, Task>>> _subscribers = new ();

    public void Publish(Message message)
    {
        var queue = _queues.GetOrAdd(message.Topic, _ => new ConcurrentQueue<Message>());
        queue.Enqueue(message);
    }

    public void Subscribe(string topic, Func<Message, Task> handler)
    {
        var handlers = _subscribers.GetOrAdd(topic, _ => new List<Func<Message, Task>>());
        handlers.Add(handler);
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var topic in _queues.Keys)
            {
                if (_queues.TryGetValue(topic, out var queue) &&
                    _subscribers.TryGetValue(topic, out var handlers))
                {
                    while (queue.TryDequeue(out var msg))
                    {
                        foreach (var handler in handlers)
                        {
                            await handler(msg);
                        }
                    }
                }
            }
            await Task.Delay(500, cancellationToken);
        }
    }
}