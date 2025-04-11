namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using Interfaces;
using LovgaBroker.Interfaces;
using LovgaCommon;
using LovgaCommon.Constants;
using Models;
using Services;

public class ConsumerGrpcClient : IConsumerGrpcClient, IDisposable
{
    public string Id { get; private set; }
    private bool _disposed;
    private string _topic;

    private readonly IReceiver _receiver;
    private readonly ILogger<ConsumerGrpcClient> _logger;
    private Channel? _channel;
    private readonly StorageService _storageService;

    public event Action<string, string> OnRegisterConsumer;
    public event Action<string, string> OnUnregisterConsumer;

    public ConsumerGrpcClient(
        IReceiver receiver,
        ILogger<ConsumerGrpcClient> logger,
        StorageService storageService)
    {
        _logger = logger;
        _storageService = storageService;
        _receiver = receiver;
    }

    public void SetUpConsumer(Channel channel, string id, string topic)
    {
        Id = id;
        _topic = topic;
        _channel = channel;
        OnRegisterConsumer?.Invoke(Id, channel.Target);
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
            _logger.LogError(e, e.Message);
            await EnqueueDeadMessage(message);
            return false;
        }
        finally
        {
            await _storageService.DeleteMessage(message.Id);
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
                    OnUnregisterConsumer?.Invoke(Id, _channel.Target);
                    _logger.LogInformation($"Consumer ID: {Id} Topic: {_topic} - unregister from gRPC channel successfully.");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Error unregistering from gRPC channel Consumer ID: {Id} Topic: {_topic}");
                }
            }
        }

        _disposed = true;
    }
}