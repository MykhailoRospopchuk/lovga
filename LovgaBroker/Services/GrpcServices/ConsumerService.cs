namespace LovgaBroker.Services.GrpcServices;

using Grpc.Core;
using Models;

public class ConsumerService : Consumer.ConsumerBase
{
    private readonly ILogger<ConsumerService> _logger;
    private readonly IMessageBroker _broker;

    public ConsumerService(IMessageBroker broker, ILogger<ConsumerService> logger)
    {
        _broker = broker;
        _logger = logger;
    }

    public override Task<Reply> SubscribeConsumer(SubscribeRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Subscribe request received from gRPC. Topic: {request.Topic}. Url: {request.Url}");
        

        _broker.Subscribe("test-topic", HandleMessageAsync);
        return Task.FromResult(new Reply
        {
            Success = true
        });
    }

    private Task HandleMessageAsync(Message message)
    {
        _logger.LogInformation($"Received message: {message.Content} at {message.CreatedAt}");
        return Task.CompletedTask;
    }
}