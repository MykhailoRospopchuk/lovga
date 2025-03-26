namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;

public class SubscriberGrpcServer : Subscriber.SubscriberBase
{
    private readonly ILogger<SubscriberGrpcServer> _logger;
    private readonly IBrokerManager _broker;
    private readonly ILoggerFactory _loggerFactory;

    public SubscriberGrpcServer(IBrokerManager broker, ILogger<SubscriberGrpcServer> logger, ILoggerFactory loggerFactory)
    {
        _broker = broker;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public override Task<Reply> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Subscribe from gRPC. Topic: {request.Topic}. Host: {request.Host}. Port: {request.Port}");

        var logger = _loggerFactory.CreateLogger<ConsumerGrpcClient>();
        var consumer = new ConsumerGrpcClient(request.Host, request.Port, request.Topic, logger);

        if (!consumer.InitChannel())
        {
            return Task.FromResult(new Reply
            {
                Success = false,
            });
        }

        var broker = _broker.GetBroker(request.Topic);
        broker.Subscribe(request.Id, consumer);
        return Task.FromResult(new Reply
        {
            Success = true,
        });
    }
}