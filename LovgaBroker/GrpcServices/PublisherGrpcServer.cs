namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;
using Models;

public class PublisherGrpcServer : Publisher.PublisherBase
{
    private readonly ILogger<PublisherGrpcServer> _logger;
    private IReceiver _receiver;

    public PublisherGrpcServer(
        ILogger<PublisherGrpcServer> logger,
        IReceiver receiver,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _receiver = receiver;

        Initialize(serviceScopeFactory);
    }

    private void Initialize(IServiceScopeFactory serviceScopeFactory)
    {
        using var scope = serviceScopeFactory.CreateScope();
        _receiver = scope.ServiceProvider.GetRequiredService<IReceiver>();
    }

    public override Task<Reply> Publish(PublishRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Published message from gRPC. Topic: {request.Topic}");
        _receiver.Publish(new Message
        {
            Topic = request.Topic,
            Content = request.Content
        });

        return Task.FromResult(new Reply
        {
            Success = true,
        });
    }
}