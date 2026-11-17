namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using System.Data;
using System.Threading.Channels;
using GrpcServices.Interfaces;
using Interfaces;
using LovgaCommon.Constants;
using Models;

public sealed class MessageBroker(
    ILogger<MessageBroker> logger,
    IReceiver receiver,
    IStorageService storageService) : IMessageBroker
{
    public string Topic { get; private set; } = string.Empty;

    private readonly Channel<Message> _queues = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, IConsumerGrpcClient> _subscribers = new();
    private readonly Lock _subscriberLock = new();
    private TaskCompletionSource _hasSubscribersSignal = CreateSignal();

    // How long to wait for a single subscriber to accept & ACK a message before evicting it.
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    // Maximum number of concurrent messages in flight per topic (prevents head-of-line blocking across RTTs).
    private const int MaxInFlightMessagesPerTopic = 64;

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
        lock (_subscriberLock)
        {
            var result = _subscribers.TryAdd(subscriberId, consumerGrpcClient);
            if (result && _subscribers.Count > 0)
            {
                _hasSubscribersSignal.TrySetResult();
            }

            return result;
        }
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
        using var concurrencyLimiter = new SemaphoreSlim(MaxInFlightMessagesPerTopic, MaxInFlightMessagesPerTopic);
        var inFlightTasks = new ConcurrentDictionary<long, Task>();
        long dispatchSequence = 0;

        try
        {
            await foreach (var message in _queues.Reader.ReadAllAsync(cancellationToken))
            {
                // Wait until at least one subscriber is registered.
                while (true)
                {
                    Task waitTask;
                    lock (_subscriberLock)
                    {
                        if (!_subscribers.IsEmpty)
                        {
                            break;
                        }

                        if (_hasSubscribersSignal.Task.IsCompleted)
                        {
                            _hasSubscribersSignal = CreateSignal();
                        }

                        waitTask = _hasSubscribersSignal.Task;
                    }

                    await waitTask.WaitAsync(cancellationToken);
                }

                await concurrencyLimiter.WaitAsync(cancellationToken);

                var seq = Interlocked.Increment(ref dispatchSequence);
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Dispatching message #{Counter} (Id={MessageId}) on topic {Topic}", seq, message.Id, Topic);
                }

                var task = DispatchSingleMessageAsync(message, concurrencyLimiter, seq, inFlightTasks, cancellationToken);
                inFlightTasks.TryAdd(seq, task);
            }
        }
        finally
        {
            if (!inFlightTasks.IsEmpty)
            {
                await Task.WhenAll(inFlightTasks.Values);
            }
        }
    }

    private async Task DispatchSingleMessageAsync(
        Message message,
        SemaphoreSlim concurrencyLimiter,
        long seq,
        ConcurrentDictionary<long, Task> inFlightTasks,
        CancellationToken cancellationToken)
    {
        try
        {
            KeyValuePair<string, IConsumerGrpcClient>[] subscribersSnapshot;
            lock (_subscriberLock)
            {
                subscribersSnapshot = [.. _subscribers];
            }

            if (subscribersSnapshot.Length == 0)
            {
                // Subscribers disconnected right before snapshot; re-enqueue for when a subscriber arrives
                await _queues.Writer.WriteAsync(message, cancellationToken);
                return;
            }

            await storageService.MarkInFlight(message.Id);

            var deliveryTasks = new Task<(bool IsDelivered, string Key)>[subscribersSnapshot.Length];
            for (int i = 0; i < subscribersSnapshot.Length; i++)
            {
                var kvp = subscribersSnapshot[i];
                deliveryTasks[i] = DeliverToSubscriberAsync(kvp.Key, kvp.Value, message, cancellationToken);
            }

            var results = await Task.WhenAll(deliveryTasks);

            var failedCount = 0;
            foreach (var (isDelivered, key) in results)
            {
                if (!isDelivered)
                {
                    failedCount++;
                    RemoveSubscriber(key);
                }
            }

            if (failedCount == subscribersSnapshot.Length)
            {
                // All subscribers failed delivery
                if (!cancellationToken.IsCancellationRequested)
                {
                    if (Topic != QueueTopic.DeadLetterQueue)
                    {
                        await EnqueueDeadMessage(message, cancellationToken);
                        await storageService.DeleteMessage(message.Id);
                    }
                    else
                    {
                        await storageService.ReleaseInFlight(message.Id);
                    }
                }
                else
                {
                    await storageService.ReleaseInFlight(message.Id);
                }
            }
            else
            {
                // Delivered and ACKed by at least one active subscriber
                await storageService.DeleteMessage(message.Id);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await storageService.ReleaseInFlight(message.Id);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error dispatching message {MessageId} on topic {Topic}", message.Id, Topic);
            await storageService.ReleaseInFlight(message.Id);
        }
        finally
        {
            inFlightTasks.TryRemove(seq, out _);
            concurrencyLimiter.Release();
        }
    }

    private async Task<(bool IsDelivered, string Key)> DeliverToSubscriberAsync(
        string subscriberId,
        IConsumerGrpcClient consumer,
        Message message,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DeliveryTimeout);
        try
        {
            var delivered = await consumer.DeliverMessage(message, cts.Token);
            if (!delivered && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Delivery failed or timed out for subscriber {Id} on topic {Topic} (MessageId={MessageId})",
                    subscriberId, Topic, message.Id);
            }

            return (delivered, subscriberId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Delivery timed out after {Timeout}s for subscriber {Id} on topic {Topic}",
                DeliveryTimeout.TotalSeconds, subscriberId, Topic);
            return (false, subscriberId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Delivery failed for subscriber {Id} on topic {Topic}", subscriberId, Topic);
            return (false, subscriberId);
        }
    }

    private bool RemoveSubscriber(string subscriberId)
    {
        bool removed;
        lock (_subscriberLock)
        {
            removed = _subscribers.TryRemove(subscriberId, out _);
            if (_subscribers.IsEmpty && _hasSubscribersSignal.Task.IsCompleted)
            {
                _hasSubscribersSignal = CreateSignal();
            }
        }

        if (removed)
        {
            logger.LogInformation("Subscriber {SubscriberId} removed from topic {Topic}", subscriberId, Topic);
        }

        return removed;
    }

    private ValueTask<bool> EnqueueDeadMessage(Message message, CancellationToken cancellationToken)
    {
        return receiver.Publish(new Message
        {
            Topic = QueueTopic.DeadLetterQueue,
            Content = message.Content,
            CreatedAt = message.CreatedAt
        }, cancellationToken);
    }
}