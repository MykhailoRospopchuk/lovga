namespace LovgaBroker.GrpcServices;

using Interfaces;
using LovgaBroker.Interfaces;
using LovgaCommon;
using Models;
using Services;

public class ConsumerGrpcClient : IConsumerGrpcClient, IDisposable
{
    public string Id { get; private set; }
    private bool _disposed;
    private string _topic;
    private string _target = string.Empty;

    private readonly ILogger<ConsumerGrpcClient> _logger;
    private readonly IChannelManger _channelManger;
    private readonly StorageService _storageService;

    public event Action<string, string> OnRegisterConsumer;
    public event Action<string, string> OnUnregisterConsumer;

    public ConsumerGrpcClient(
        ILogger<ConsumerGrpcClient> logger,
        StorageService storageService,
        IChannelManger channelManger)
    {
        _logger = logger;
        _storageService = storageService;
        _channelManger = channelManger;
    }

    public void SetUpConsumer(string id, string topic, string target)
    {
        Id = id;
        _topic = topic;
        _target = target;
        OnRegisterConsumer?.Invoke(_target, Id);
    }

    public bool DeliverMessage(Message message) 
    {
        if (message.Topic != _topic)
        {
            _logger.LogError($"Invalid topic {_topic}");
            return false;
        }

        var channel = _channelManger?.GetChannel(_target);

        if (channel is null)
        {
            _logger.LogError("Channel is not initialized");
            return false;
        }

        try
        {
            var client = new Consumer.ConsumerClient(channel);

            var reply = client.Notify(new NotifyRequest
            {
                Topic = message.Topic,
                Content = message.Content,
            });

            if (!reply.Success)
            {
                _logger.LogError("Error. Consumer failed to notify");
            }

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            return false;
        }
        finally
        {
            _storageService.DeleteMessage(message.Id).GetAwaiter();
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            try
            {
                OnUnregisterConsumer?.Invoke(_target, Id);
                _logger.LogInformation($"Consumer ID: {Id} Topic: {_topic} - unregister from gRPC channel successfully.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error unregistering from gRPC channel Consumer ID: {Id} Topic: {_topic}");
            }
        }

        _disposed = true;
    }
}