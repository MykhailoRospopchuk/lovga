namespace LovgaBroker.Services.GrpcServices;

using Grpc.Core;
using LovgaCommon;
using Models;

public class SubscriberService : Subscriber.SubscriberBase
{
    private readonly ILogger<SubscriberService> _logger;
    private readonly IMessageBroker _broker;

    public SubscriberService(IMessageBroker broker, ILogger<SubscriberService> logger)
    {
        _broker = broker;
        _logger = logger;
    }

    public override Task<Reply> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Subscribe request received from gRPC. Topic: {request.Topic}. Url: {request.Url}");

        _broker.Subscribe("test-topic", HandleMessageAsync);
        _broker.Subscribe(request.Topic, HandleMessageAsync);
        return Task.FromResult(new Reply
        {
            Success = true,
        });
    }

    private Task HandleMessageAsync(Message message)
    {
        _logger.LogInformation($"Send message to subscriber at {message.CreatedAt}");

        var channel = new Channel("localhost:7080", ChannelCredentials.Insecure);
        var client = new Consumer.ConsumerClient(channel);

        var reply = client.Notify(new NotifyRequest
        {
            Test = "This is me - BOBR!"
        });

        if (!reply.Success)
        {
            _logger.LogError("Error");
        }

        return Task.CompletedTask;
    }
}