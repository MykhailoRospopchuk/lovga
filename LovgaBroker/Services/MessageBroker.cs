namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;
using GrpcServices;
using GrpcServices.Interfaces;
using Interfaces;
using Models;

public class MessageBroker : IMessageBroker
{
    public string Topic { get; }
    private readonly ILogger<MessageBroker> _logger;

    private readonly Channel<Message> _queues = Channel.CreateUnbounded<Message>();
    private readonly ConcurrentDictionary<string, IConsumerGrpcClient> _subscribers = new ();
    private readonly SemaphoreSlim _subscriberSignal = new(0);
    private readonly Lock _subscriberLock = new();

    public MessageBroker(string topic, ILogger<MessageBroker> logger)
    {
        Topic = topic;
        _logger = logger;
    }

    public ValueTask EnqueueMessage(Message message)
    {
        return _queues.Writer.WriteAsync(message);
    }

    public bool Subscribe(string subscriberId, IConsumerGrpcClient consumerGrpcClient)
    {
        var result = _subscribers.TryAdd(subscriberId, consumerGrpcClient);
        if (_subscribers.Count == 1 && result)
        {
            _subscriberSignal.Release();
        }

        return result;
    }

    public bool Unsubscribe(string subscriberId)
    {
        return RemoveSubscriber(subscriberId);
    }

    public bool ConsumerExists(string subscriberId)
    {
        return _subscribers.ContainsKey(subscriberId);
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

            var keyToRemove = new List<string>();
            _logger.LogInformation($"{counter++}/{_queues.Reader.Count}");
            try
            {
                foreach (var handler in _subscribers)
                {
                    if (!await handler.Value.DeliverMessage(message))
                    {
                        keyToRemove.Add(handler.Key);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
            finally
            {
                foreach (var key in keyToRemove)
                {
                    RemoveSubscriber(key);
                }
            }
        }
    }

    private bool RemoveSubscriber(string subscriberId)
    {
        if (_subscribers.TryRemove(subscriberId, out IConsumerGrpcClient? existConsumer))
        {
            if (existConsumer is ConsumerGrpcClient cgClient)
            {
                cgClient.Dispose();
                return true;
            }
        }

        return false;
    }
}