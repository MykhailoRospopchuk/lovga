namespace LovgaBroker.GrpcServices;

using System.Collections.Concurrent;
using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;
using Services;

public class SubscriberGrpcServer : Subscriber.SubscriberBase
{
    private readonly ILogger<SubscriberGrpcServer> _logger;
    private readonly IBrokerManager _brokerManager;
    private readonly IStorageService _storageService;

    public SubscriberGrpcServer(
        IBrokerManager brokerManager,
        ILogger<SubscriberGrpcServer> logger,
        IStorageService storageService)
    {
        _brokerManager = brokerManager;
        _logger = logger;
        _storageService = storageService;
    }

    public override async Task SubscribeStream(
        IAsyncStreamReader<ClientMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        var activeSubscriptions = new ConcurrentDictionary<(string Topic, string ConsumerId), StreamingConsumer>();

        try
        {
            await foreach (var clientMsg in requestStream.ReadAllAsync(context.CancellationToken))
            {
                switch (clientMsg.PayloadCase)
                {
                    case ClientMessage.PayloadOneofCase.Subscribe:
                        var sub = clientMsg.Subscribe;
                        var group = string.IsNullOrEmpty(sub.ConsumerGroup) ? sub.ConsumerId : sub.ConsumerGroup;
                        var key = (sub.Topic, sub.ConsumerId);
                        if (!activeSubscriptions.ContainsKey(key))
                        {
                            var consumer = new StreamingConsumer(sub.ConsumerId, group, sub.Topic, responseStream, _logger);
                            var broker = _brokerManager.GetBroker(sub.Topic);
                            if (broker.Subscribe(sub.ConsumerId, group, consumer))
                            {
                                activeSubscriptions.TryAdd(key, consumer);
                                _ = Task.Run(async () => await consumer.ProcessLoopAsync());
                                _logger.LogInformation("Subscriber {Id} (Group: {Group}) successfully subscribed to topic {Topic} via stream", sub.ConsumerId, group, sub.Topic);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to subscribe {Id} to topic {Topic}", sub.ConsumerId, sub.Topic);
                            }
                        }
                        break;

                    case ClientMessage.PayloadOneofCase.Unsubscribe:
                        var unsub = clientMsg.Unsubscribe;
                        var unsubKey = (unsub.Topic, unsub.ConsumerId);
                        if (activeSubscriptions.TryRemove(unsubKey, out var existingConsumer))
                        {
                            var broker = _brokerManager.GetBroker(unsub.Topic);
                            var unsubGroup = existingConsumer.ConsumerGroupId;
                            broker.Unsubscribe(unsub.ConsumerId, unsubGroup);
                            existingConsumer.CancelAll();
                            _logger.LogInformation("Subscriber {Id} unsubscribed from topic {Topic}", unsub.ConsumerId, unsub.Topic);
                        }
                        break;

                    case ClientMessage.PayloadOneofCase.Ack:
                        var ack = clientMsg.Ack;
                        foreach (var entry in activeSubscriptions)
                        {
                            if (entry.Key.Topic == ack.Topic)
                            {
                                var broker = _brokerManager.GetBroker(ack.Topic);
                                broker.Acknowledge(entry.Value.ConsumerGroupId, ack.MessageId);
                            }
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Subscriber stream cancelled by client/server.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in subscriber stream");
        }
        finally
        {
            foreach (var entry in activeSubscriptions)
            {
                var broker = _brokerManager.GetBroker(entry.Key.Topic);
                broker.Unsubscribe(entry.Key.ConsumerId, entry.Value.ConsumerGroupId);
                entry.Value.CancelAll();
                _logger.LogInformation("Cleaned up subscription for {Id} on topic {Topic} due to stream close", entry.Key.ConsumerId, entry.Key.Topic);
            }
        }
    }
}