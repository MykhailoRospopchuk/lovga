namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Interfaces;
using Models;

public class MessageBroker : IMessageBroker
{
    public string Topic { get; }

    private readonly Channel<Message> _queues = Channel.CreateUnbounded<Message>();
    private readonly ConcurrentDictionary<string, IConsumer> _subscribers = new ();
    private readonly SemaphoreSlim _subscriberSignal = new(0);
    private readonly object _subscriberLock = new();

    public MessageBroker(string topic)
    {
        Topic = topic;
    }

    public ValueTask EnqueueMessage(Message message)
    {
        return _queues.Writer.WriteAsync(message);
    }

    public void Subscribe(string subscriberId, IConsumer consumer)
    {
        _subscribers.TryAdd(subscriberId, consumer);
        if (_subscribers.Count == 1)
        {
            _subscriberSignal.Release();
        }
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        int counter = 0;
        await foreach (var message in _queues.Reader.ReadAllAsync(cancellationToken))
        {
            while (true)
            {
                lock (_subscriberLock)
                {
                    if (_subscribers.Count > 0)
                        break;
                }
                await _subscriberSignal.WaitAsync(cancellationToken);
            }

            Console.WriteLine($"{counter++}/{_queues.Reader.Count}");
            foreach (var handler in _subscribers)
            {
                await handler.Value.DeliverMessage(message);
            }
        }
    }
}