namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using System.Threading.Channels;
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

    public void Subscribe(string subscriberId, IConsumerGrpcClient consumerGrpcClient)
    {
        _subscribers.TryAdd(subscriberId, consumerGrpcClient);
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
                    _subscribers.TryRemove(key, out _);
                }
            }
        }
    }
}