namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;
using Services;

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
        var broker = _brokerManager.GetBroker(request.Topic);

        if (broker.ConsumerExists(request.Id))
        {
            return Task.FromResult(new Reply
            {
                Success = false,
            });
        }

        var logger = _serviceProvider.GetRequiredService<ILogger<ConsumerGrpcClient>>();
        var receiver = _serviceProvider.GetRequiredService<IReceiver>();
        var storage = _serviceProvider.GetRequiredService<StorageService>();
        var channel = _channelManager.GetChannel(request.Host, request.Port);

        if (channel is null)
        {
            _logger.LogInformation($"Channel {request.Host}:{request.Port} not found or created");
            return Task.FromResult(new Reply
            {
                Success = true, // TODO: need investigate what to do in that case
            });
        }
        var consumer = new ConsumerGrpcClient(request.Id, request.Topic, receiver, logger, storage);
        consumer.OnRegisterConsumer += _channelManager.RegisterConsumer;
        consumer.OnUnregisterConsumer += _channelManager.UnregisterConsumer;
        consumer.SetUpChannel(channel);

        var result = broker.Subscribe(request.Id, consumer);
        if (!result)
        {
            consumer.Dispose();
        }

        _logger.LogInformation($"Subscribe from gRPC. Topic: {request.Topic}. Host: {request.Host}. Port: {request.Port}");
        return Task.FromResult(new Reply
        {
            Success = result,
        });
    }

    public override Task<Reply> UnSubscribe(UnsubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Un Subscribe from gRPC. Topic: {request.Topic}. Id: {request.Id}");
        var broker = _brokerManager.GetBroker(request.Topic);
        var result = broker.Unsubscribe(request.Id);

        return Task.FromResult(new Reply
        {
            Success = result,
        });
    }
}