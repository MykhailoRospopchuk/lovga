namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using Interfaces;
using LovgaBroker.Interfaces;
using LovgaCommon;

public class SubscriberGrpcServer : Subscriber.SubscriberBase
{
    private readonly ILogger<SubscriberGrpcServer> _logger;
    private readonly IBrokerManager _brokerManager;
    private readonly IChannelManger _channelManager;
    private readonly IServiceProvider _serviceProvider;

    public SubscriberGrpcServer(
        IBrokerManager brokerManager,
        ILogger<SubscriberGrpcServer> logger,
        IServiceProvider serviceProvider,
        IChannelManger channelManager)
    {
        _brokerManager = brokerManager;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _channelManager = channelManager;
    }

    public override Task<Reply> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Host);
        ArgumentException.ThrowIfNullOrEmpty(request.Id);
        ArgumentException.ThrowIfNullOrEmpty(request.Topic);

        if (request.Port < 0)
        {
            throw new ArgumentException("Port cannot be negative", nameof(request.Port));
        }

        var broker = _brokerManager.GetBroker(request.Topic);

        if (broker.ConsumerExists(request.Id))
        {
            return Task.FromResult(new Reply
            {
                Success = false,
            });
        }

        var target = $"{request.Host}:{request.Port}";
        var channel = _channelManager.ChannelExists(target);

        if (!channel)
        {
            _logger.LogInformation($"Channel {request.Host}:{request.Port} not found or created");
            return Task.FromResult(new Reply
            {
                Success = false
            });
        }

        var consumer = _serviceProvider.GetRequiredService<IConsumerGrpcClient>();
        consumer.OnRegisterConsumer += _channelManager.RegisterConsumer;
        consumer.OnUnregisterConsumer += _channelManager.UnregisterConsumer;
        consumer.SetUpConsumer(request.Id, request.Topic, target);

        var result = broker.Subscribe(request.Id, consumer);

        _logger.LogInformation($"{0} attempt to subscribe. Topic: {request.Topic} Id: {request.Id}. Host: {request.Host}. Port: {request.Port}", result ? "Success" : "Failed");
        return Task.FromResult(new Reply
        {
            Success = result,
        });
    }

    public override Task<Reply> UnSubscribe(UnsubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Attempt to Unsubscribe from gRPC. Topic: {request.Topic} Id: {request.Id}");
        var broker = _brokerManager.GetBroker(request.Topic);
        var result = broker.Unsubscribe(request.Id);

        return Task.FromResult(new Reply
        {
            Success = result,
        });
    }
}