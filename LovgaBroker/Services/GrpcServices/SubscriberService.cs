namespace LovgaBroker.Services.GrpcServices;

using Grpc.Core;
using LovgaCommon;

public class SubscriberService : Subscriber.SubscriberBase
{
    private readonly ILogger<SubscriberService> _logger;
    private readonly IMessageBroker _broker;
    private readonly ILoggerFactory _loggerFactory;

    public SubscriberService(IMessageBroker broker, ILogger<SubscriberService> logger, ILoggerFactory loggerFactory)
    {
        _broker = broker;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public override Task<Reply> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Subscribe from gRPC. Topic: {request.Topic}. Host: {request.Host}. Port: {request.Port}");

        var logger = _loggerFactory.CreateLogger<ConsumerService>();
        var consumer = new ConsumerService(request.Host, request.Port, request.Topic, logger);

        if (!consumer.InitChannel())
        {
            return Task.FromResult(new Reply
            {
                Success = false,
            });
        }

        _broker.Subscribe(request.Topic, consumer);
        return Task.FromResult(new Reply
        {
            Success = true,
        });
    }
}