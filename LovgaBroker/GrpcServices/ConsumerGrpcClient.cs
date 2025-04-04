namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using Interfaces;
using LovgaBroker.Interfaces;
using LovgaCommon;
using LovgaCommon.Constants;
using Models;

public class ConsumerGrpcClient : IConsumerGrpcClient, IDisposable
{
    private bool _disposed;

    private readonly string _topic;

    private readonly IReceiver _receiver;
    private readonly ILogger<ConsumerGrpcClient> _logger;
    private Channel? _channel;

    public string Id { get; }

    public ConsumerGrpcClient(
        string id,
        string topic,
        IReceiver receiver,
        ILogger<ConsumerGrpcClient> logger,
        Channel? channel)
    {
        Id = id;
        _topic = topic;
        _logger = logger;
        _channel = channel;
        _receiver = receiver;
    }

    public async Task<bool> DeliverMessage(Message message) 
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Channel is not initialized");
        }

        if (message.Topic != _topic)
        {
            _logger.LogError($"Invalid topic {_topic}");
            await EnqueueDeadMessage(message);
            return false;
        }

        try
        {
            var client = new Consumer.ConsumerClient(_channel);

            var reply = await client.NotifyAsync(new NotifyRequest
            {
                Topic = message.Topic,
                Content = message.Content,
            });

            if (!reply.Success)
            {
                _logger.LogError("Error. Consumer failed to notify");
                await EnqueueDeadMessage(message);
            }

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            await EnqueueDeadMessage(message);
            return false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
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

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (_channel != null)
            {
                try
                {
                    _channel.ShutdownAsync().GetAwaiter().GetResult();
                    _logger.LogInformation($"Consumer ID: {Id} Topic: {_topic} - gRPC channel shutdown successfully.");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Error shutting down gRPC channel Consumer ID: {Id} Topic: {_topic}");
                }
            }
        }

        _disposed = true;
    }
}