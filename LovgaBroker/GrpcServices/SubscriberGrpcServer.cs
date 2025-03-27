namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;

public class SubscriberGrpcServer : Subscriber.SubscriberBase
{
    private readonly ILogger<SubscriberGrpcServer> _logger;
    private readonly IBrokerManager _brokerManager;
    private readonly ILoggerFactory _loggerFactory;

    public SubscriberGrpcServer(IBrokerManager brokerManager, ILogger<SubscriberGrpcServer> logger, ILoggerFactory loggerFactory)
    {
        _brokerManager = brokerManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public override Task<Reply> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Subscribe from gRPC. Topic: {request.Topic}. Host: {request.Host}. Port: {request.Port}");

        var logger = _loggerFactory.CreateLogger<ConsumerGrpcClient>();
        var consumer = new ConsumerGrpcClient(request.Id, request.Host, request.Port, request.Topic, logger);

        if (!consumer.InitChannel())
        {
            return Task.FromResult(new Reply
            {
                Success = false,
            });
        }

        var broker = _brokerManager.GetBroker(request.Topic);
        var result = broker.Subscribe(request.Id, consumer);
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