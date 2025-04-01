namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using Interfaces;
using LovgaBroker.Interfaces;
using LovgaCommon;
using LovgaCommon.Constants;
using Models;

public class ConsumerGrpcClient : IConsumerGrpcClient, IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _topic;
    private readonly ILogger<ConsumerGrpcClient> _logger;
    private readonly IReceiver _receiver;
    private Channel? _channel;
    private bool _disposed;

    public string Id { get; }

    public ConsumerGrpcClient(
        string id,
        string host,
        int port,
        string topic,
        IReceiver receiver,
        ILogger<ConsumerGrpcClient> logger)
    {
        Id = id;
        _host = host;
        _port = port;
        _topic = topic;
        _logger = logger;
        _receiver = receiver;
    }

    public bool InitChannel()
    {
        try
        {
            _channel = new Channel(_host, _port, ChannelCredentials.Insecure);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error initializing channel");
            return false;
        }

        return true;
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