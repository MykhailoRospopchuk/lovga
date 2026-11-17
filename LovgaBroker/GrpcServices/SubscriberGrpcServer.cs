namespace LovgaBroker.GrpcServices;

using System.Collections.Concurrent;
using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;

public sealed class SubscriberGrpcServer(
    IBrokerManager brokerManager,
    ILogger<SubscriberGrpcServer> logger) : Subscriber.SubscriberBase
{
    public override async Task SubscribeStream(
        IAsyncStreamReader<ClientMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        var activeSubscriptions = new ConcurrentDictionary<(string Topic, string ConsumerId), StreamingConsumer>();
        using var streamWriteLock = new SemaphoreSlim(1, 1);

        try
        {
            await foreach (var clientMsg in requestStream.ReadAllAsync(context.CancellationToken))
            {
                switch (clientMsg.PayloadCase)
                {
                    case ClientMessage.PayloadOneofCase.Subscribe:
                        var sub = clientMsg.Subscribe;
                        var key = (sub.Topic, sub.ConsumerId);
                        if (!activeSubscriptions.ContainsKey(key))
                        {
                            var consumer = new StreamingConsumer(sub.ConsumerId, responseStream, streamWriteLock, logger);
                            var broker = brokerManager.GetBroker(sub.Topic);
                            if (broker.Subscribe(sub.ConsumerId, consumer))
                            {
                                activeSubscriptions.TryAdd(key, consumer);
                                logger.LogInformation("Subscriber {Id} successfully subscribed to topic {Topic} via stream", sub.ConsumerId, sub.Topic);
                            }
                            else
                            {
                                logger.LogWarning("Failed to subscribe {Id} to topic {Topic}", sub.ConsumerId, sub.Topic);
                            }
                        }
                        break;

                    case ClientMessage.PayloadOneofCase.Unsubscribe:
                        var unsub = clientMsg.Unsubscribe;
                        var unsubKey = (unsub.Topic, unsub.ConsumerId);
                        if (activeSubscriptions.TryRemove(unsubKey, out var existingConsumer))
                        {
                            var broker = brokerManager.GetBroker(unsub.Topic);
                            broker.Unsubscribe(unsub.ConsumerId);
                            existingConsumer.CancelAll();
                            logger.LogInformation("Subscriber {Id} unsubscribed from topic {Topic}", unsub.ConsumerId, unsub.Topic);
                        }
                        break;

                    case ClientMessage.PayloadOneofCase.Ack:
                        var ack = clientMsg.Ack;
                        foreach (var entry in activeSubscriptions)
                        {
                            if (entry.Key.Topic == ack.Topic)
                            {
                                entry.Value.Acknowledge(ack.MessageId);
                            }
                        }
                        break;
                }
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            logger.LogInformation("Subscriber stream cancelled by client/server.");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Subscriber stream cancelled by client/server.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred in subscriber stream");
        }
        finally
        {
            foreach (var entry in activeSubscriptions)
            {
                var broker = brokerManager.GetBroker(entry.Key.Topic);
                broker.Unsubscribe(entry.Key.ConsumerId);
                entry.Value.CancelAll();
                logger.LogInformation("Cleaned up subscription for {Id} on topic {Topic} due to stream close", entry.Key.ConsumerId, entry.Key.Topic);
            }
        }
    }
}