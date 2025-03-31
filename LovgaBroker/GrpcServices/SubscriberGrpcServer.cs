namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;

public class SubscriberGrpcServer : Subscriber.SubscriberBase
{
    private readonly ILogger<SubscriberGrpcServer> _logger;
    private readonly IBrokerManager _brokerManager;
    private readonly IServiceProvider _serviceProvider;

    public SubscriberGrpcServer(
        IBrokerManager brokerManager,
        ILogger<SubscriberGrpcServer> logger,
        IServiceProvider serviceProvider)
    {
        _brokerManager = brokerManager;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public override Task<Reply> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Subscribe from gRPC. Topic: {request.Topic}. Host: {request.Host}. Port: {request.Port}");

        var logger = _serviceProvider.GetRequiredService<ILogger<ConsumerGrpcClient>>();
        var receiver = _serviceProvider.GetRequiredService<IReceiver>();
        var consumer = new ConsumerGrpcClient(request.Id, request.Host, request.Port, request.Topic, receiver, logger);

        if (!consumer.InitChannel())
        {
            return Task.FromResult(new Reply
            {
                Success = false,
            });
        }

        var broker = _brokerManager.GetBroker(request.Topic);
        var result = broker.Subscribe(request.Id, consumer);

        if (!result)
        {
            consumer.Dispose();
        }

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