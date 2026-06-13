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
    private readonly StorageService _storageService;

    private readonly Channel<Message> _queues = Channel.CreateUnbounded<Message>();
    private readonly ConcurrentDictionary<string, IConsumerGrpcClient> _subscribers = new();
    private readonly SemaphoreSlim _subscriberSignal = new(0);
    private readonly Lock _subscriberLock = new();

    // How long to wait for a single subscriber to accept a message before giving up.
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    public MessageBroker(ILogger<MessageBroker> logger, IReceiver receiver, StorageService storageService)
    {
        _logger = logger;
        _receiver = receiver;
        _storageService = storageService;
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
        bool result;
        lock (_subscriberLock)
        {
            result = _subscribers.TryAdd(subscriberId, consumerGrpcClient);

            // Signal is released inside the lock so the count read and Release() are atomic.
            // Previously, a concurrent Unsubscribe between TryAdd and Count check could
            // cause the signal to be skipped → dispatch loop stuck waiting forever.
            if (result && _subscribers.Count == 1)
            {
                _subscriberSignal.Release();
            }
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
            // Wait until at least one subscriber is registered.
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
            // Capture subscriber count BEFORE delivery so the finally block can compare correctly.
            var originalCount = _subscribers.Count;
            _logger.LogInformation("Dispatching message #{Counter} on topic {Topic}", counter++, Topic);
            try
            {
                // Fan-out to all subscribers in parallel.
                // No Task.Run needed — DeliverMessage is already async and executes on the thread pool.
                // Each delivery gets its own timeout linked to the dispatch cancellation token.
                var deliveryTasks = _subscribers.Select(async kvp =>
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(DeliveryTimeout);
                    try
                    {
                        var delivered = await kvp.Value.DeliverMessage(message);
                        return (IsDelivered: delivered, Key: kvp.Key);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Delivery timed out for this specific subscriber — don't propagate to others.
                        _logger.LogWarning(
                            "Delivery timed out after {Timeout}s for subscriber {Id} on topic {Topic}",
                            DeliveryTimeout.TotalSeconds, kvp.Key, Topic);
                        return (IsDelivered: false, Key: kvp.Key);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Delivery failed for subscriber {Id} on topic {Topic}", kvp.Key, Topic);
                        return (IsDelivered: false, Key: kvp.Key);
                    }
                });

                var results = await Task.WhenAll(deliveryTasks);

                keyToRemove = results
                    .Where(t => !t.IsDelivered)
                    .Select(t => t.Key)
                    .ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
            }
            finally
            {
                foreach (var key in keyToRemove)
                {
                    RemoveSubscriber(key);
                }

                // If delivery failed for every subscriber, route to the dead-letter queue.
                if (keyToRemove.Count == originalCount)
                {
                    await EnqueueDeadMessage(message);
                    await _storageService.DeleteMessage(message.Id);
                }
            }
        }
    }

    private bool RemoveSubscriber(string subscriberId)
    {
        if (_subscribers.TryRemove(subscriberId, out _))
        {
            _logger.LogInformation("Subscriber {SubscriberId} removed from topic {Topic}", subscriberId, Topic);
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