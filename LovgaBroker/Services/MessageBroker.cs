namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Models;

public class MessageBroker : IMessageBroker
{
    private readonly Channel<Message> _queues = Channel.CreateUnbounded<Message>();
    private readonly ConcurrentDictionary<string, List<Func<Message, Task>>> _subscribers = new ();

    public ValueTask Publish(Message message)
    {
        return _queues.Writer.WriteAsync(message);
    }

    public void Subscribe(string topic, Func<Message, Task> handler)
    {
        var handlers = _subscribers.GetOrAdd(topic, _ => new List<Func<Message, Task>>());
        handlers.Add(handler);
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        int counter = 0;
        await foreach (var message in _queues.Reader.ReadAllAsync(cancellationToken))
        {
            Console.WriteLine($"{counter++}/{_queues.Reader.Count}");
            if (_subscribers.TryGetValue(message.Topic, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    await handler(message);
                }
            }
        }
    }
}