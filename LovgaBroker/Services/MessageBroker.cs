namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using System.Data;
using System.Threading.Channels;
using GrpcServices.Interfaces;
using Interfaces;
using LovgaCommon.Constants;
using Models;

public class MessageBroker : IMessageBroker
{
    public string Topic { get; private set; }
    private readonly ILogger<MessageBroker> _logger;
    private readonly IReceiver _receiver;

    private readonly Channel<Message> _queues = Channel.CreateUnbounded<Message>();
    private readonly ConcurrentDictionary<string, IConsumerGrpcClient> _subscribers = new ();
    private readonly SemaphoreSlim _subscriberSignal = new(0);
    private readonly Lock _subscriberLock = new();

    public MessageBroker(ILogger<MessageBroker> logger, IReceiver receiver)
    {
        _logger = logger;
        _receiver = receiver;
    }

    public void SetTopic(string topic)
    {
        if (!string.IsNullOrEmpty(Topic))
        {
            throw new NoNullAllowedException("Topic cannot be changed.");
        }

        ArgumentException.ThrowIfNullOrEmpty(topic);
        Topic = topic;
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

            var failCount = 0;
            var keyToRemove = new List<string>();
            _logger.LogInformation($"{counter++}/{_queues.Reader.Count}");
            try
            {
                foreach (var handler in _subscribers)
                {
                    if (!await handler.Value.DeliverMessage(message))
                    {
                        ++failCount;
                        keyToRemove.Add(handler.Key);
                    }
                }
            }
            catch (Exception e)
            {
                ++failCount;
                _logger.LogError(e, e.Message);
            }
            finally
            {
                foreach (var key in keyToRemove)
                {
                    RemoveSubscriber(key);
                }

                // If all message delivering failed for all consumers - then enqueue to dead queue
                if (failCount == _subscribers.Count)
                {
                    await EnqueueDeadMessage(message);
                }
            }
        }
    }

    private bool RemoveSubscriber(string subscriberId)
    {
        if (_subscribers.TryRemove(subscriberId, out _))
        {
            _logger.LogInformation($"Subscriber ID: {subscriberId} removed from {Topic} in MessageBroker");
            return true;
        }

        return false;
    }

    private ValueTask EnqueueDeadMessage(Message message)
    {
        return _receiver.Publish(new Message
        {
            Topic = QueueTopic.DeadLetterQueue,
            Content = message.Content,
            CreatedAt = message.CreatedAt
        });
    }
}