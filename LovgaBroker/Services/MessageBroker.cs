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
    private readonly IStorageService _storageService;

    // We no longer loop on _queues in MessageBroker. 
    // Instead, EnqueueMessage distributes to group dispatchers.
    private readonly ConcurrentDictionary<string, ConsumerGroupDispatcher> _consumerGroups = new();

    public MessageBroker(ILogger<MessageBroker> logger, IStorageService storageService)
    {
        _logger = logger;
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
        // Give to each active group dispatcher
        foreach (var group in _consumerGroups.Values)
        {
            group.EnqueueHotMessage(message);
        }
        return ValueTask.CompletedTask;
    }

    public bool Subscribe(string subscriberId, string consumerGroupId, IConsumerGrpcClient consumerGrpcClient)
    {
        var group = _consumerGroups.GetOrAdd(consumerGroupId, id => new ConsumerGroupDispatcher(id, Topic, _storageService, _logger));
        return group.AddSubscriber(subscriberId, consumerGrpcClient);
    }

    public bool Unsubscribe(string subscriberId, string consumerGroupId)
    {
        if (_consumerGroups.TryGetValue(consumerGroupId, out var group))
        {
            return group.RemoveSubscriber(subscriberId);
        }
        return false;
    }

    public bool ConsumerExists(string subscriberId, string consumerGroupId)
    {
        if (_consumerGroups.TryGetValue(consumerGroupId, out var group))
        {
            return group.ConsumerExists(subscriberId);
        }
        return false;
    }

    public void Acknowledge(string consumerGroupId, int messageId)
    {
        if (_consumerGroups.TryGetValue(consumerGroupId, out var group))
        {
            group.Acknowledge(messageId);
        }
    }

    public Task DispatchAsync(CancellationToken cancellationToken)
    {
        // MessageBroker no longer runs a central dispatch loop.
        // The individual ConsumerGroupDispatchers run their own loops.
        return Task.Delay(Timeout.Infinite, cancellationToken);
    }
}

public class ConsumerGroupDispatcher
{
    public string ConsumerGroupId { get; }
    public string Topic { get; }

    private readonly IStorageService _storageService;
    private readonly ILogger _logger;
    private readonly Channel<Message> _hotMessages = Channel.CreateBounded<Message>(
        new BoundedChannelOptions(10000) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly List<IConsumerGrpcClient> _subscribers = new();
    private int _roundRobinIndex = 0;
    private readonly Lock _lock = new();

    private int _lastDispatchedId = 0;
    private int _lastAckedId = 0;
    private int _lastSavedOffset = 0;
    private readonly CancellationTokenSource _cts = new();

    public ConsumerGroupDispatcher(string consumerGroupId, string topic, IStorageService storageService, ILogger logger)
    {
        ConsumerGroupId = consumerGroupId;
        Topic = topic;
        _storageService = storageService;
        _logger = logger;

        _ = Task.Run(() => ProcessLoopAsync(_cts.Token));
    }

    public bool AddSubscriber(string subscriberId, IConsumerGrpcClient client)
    {
        lock (_lock)
        {
            if (_subscribers.Any(s => s.Id == subscriberId)) return false;
            _subscribers.Add(client);
            return true;
        }
    }

    public bool RemoveSubscriber(string subscriberId)
    {
        lock (_lock)
        {
            return _subscribers.RemoveAll(s => s.Id == subscriberId) > 0;
        }
    }

    public bool ConsumerExists(string subscriberId)
    {
        lock (_lock)
        {
            return _subscribers.Any(s => s.Id == subscriberId);
        }
    }

    public void EnqueueHotMessage(Message msg)
    {
        _hotMessages.Writer.TryWrite(msg);
    }

    public void Acknowledge(int messageId)
    {
        lock (_lock)
        {
            if (messageId > _lastAckedId)
            {
                _lastAckedId = messageId;
            }
        }
    }

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        try
        {
            _lastAckedId = await _storageService.GetSubscriberOffsetAsync(ConsumerGroupId, Topic);
            _lastDispatchedId = _lastAckedId;
            _lastSavedOffset = _lastAckedId;

            _ = Task.Run(() => PeriodicSaveOffsetAsync(ct));

            while (!ct.IsCancellationRequested)
            {
                bool hasHot = _hotMessages.Reader.TryPeek(out var hotMsg);
                
                if (hasHot && hotMsg.Id == _lastDispatchedId + 1)
                {
                    _hotMessages.Reader.TryRead(out var msg);
                    await DispatchToNextConsumer(msg, ct);
                    _lastDispatchedId = msg.Id;
                    continue;
                }

                if (hasHot && hotMsg.Id <= _lastDispatchedId)
                {
                    // Already dispatched this message.
                    _hotMessages.Reader.TryRead(out _);
                    continue;
                }

                // Gap detected or no hot messages. Query DB.
                var missing = _storageService.GetMessagesAsync(Topic, _lastDispatchedId, 500);
                int count = 0;
                await foreach (var msg in missing.WithCancellation(ct))
                {
                    await DispatchToNextConsumer(msg, ct);
                    _lastDispatchedId = msg.Id;
                    count++;
                }

                if (count == 0 && !hasHot)
                {
                    // No missing messages in DB, and no hot messages.
                    // Wait for hot message to arrive.
                    await _hotMessages.Reader.WaitToReadAsync(ct);
                }
                else if (count == 0 && hasHot && hotMsg.Id > _lastDispatchedId + 1)
                {
                    // Gap detected but DB has no messages? 
                    // This means publisher hasn't committed to DB yet, but hit the hot queue.
                    // We must wait a tiny bit for DB to commit.
                    await Task.Delay(10, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in group dispatcher {Group} on topic {Topic}", ConsumerGroupId, Topic);
        }
    }

    private async Task DispatchToNextConsumer(Message msg, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IConsumerGrpcClient selectedConsumer = null;
            lock (_lock)
            {
                if (_subscribers.Count > 0)
                {
                    var index = _roundRobinIndex % _subscribers.Count;
                    selectedConsumer = _subscribers[index];
                    _roundRobinIndex = (index + 1) % _subscribers.Count;
                }
            }

            if (selectedConsumer == null)
            {
                await Task.Delay(100, ct);
                continue;
            }

            // Backpressure
            while (_lastDispatchedId - _lastAckedId > 1000 && !ct.IsCancellationRequested)
            {
                await Task.Delay(50, ct);
            }

            try
            {
                await selectedConsumer.DeliverMessage(msg);
                break;
            }
            catch
            {
                RemoveSubscriber(selectedConsumer.Id);
            }
        }
    }

    private async Task PeriodicSaveOffsetAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
                int ackToSave;
                lock (_lock)
                {
                    ackToSave = _lastAckedId;
                }

                if (ackToSave > _lastSavedOffset)
                {
                    await _storageService.SaveOffsetAsync(ConsumerGroupId, Topic, ackToSave);
                    _lastSavedOffset = ackToSave;
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
