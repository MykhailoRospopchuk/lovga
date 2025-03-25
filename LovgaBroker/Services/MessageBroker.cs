namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Interfaces;
using Models;

public class MessageBroker : IMessageBroker
{
    private readonly Channel<Message> _queues = Channel.CreateUnbounded<Message>();
    private readonly ConcurrentDictionary<string, IConsumerObserver> _subscribers = new ();

    public ValueTask Publish(Message message)
    {
        return _queues.Writer.WriteAsync(message);
    }

    public void Subscribe(string topic, IConsumerObserver consumer)
    {
        var consumerAdded = _subscribers.TryAdd(topic, consumer);
        if (!consumerAdded)
        {
            throw new InvalidOperationException($"Cannot subscribe to {topic} because it already exists.");
        }
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        int counter = 0;
        await foreach (var message in _queues.Reader.ReadAllAsync(cancellationToken))
        {
            Console.WriteLine($"{counter++}/{_queues.Reader.Count}");
            if (_subscribers.TryGetValue(message.Topic, out var handler))
            {
                await handler.DeliverMessage(message);
            }
        }
    }
}