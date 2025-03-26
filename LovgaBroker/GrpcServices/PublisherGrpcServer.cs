namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using Interfaces;
using LovgaBroker.Interfaces;
using LovgaCommon;
using Models;

public class PublisherGrpcServer : Publisher.PublisherBase
{
    private readonly ILogger<PublisherGrpcServer> _logger;
    private readonly IReceiver _receiver;

    public PublisherGrpcServer(ILogger<PublisherGrpcServer> logger, IReceiver receiver)
    {
        _logger = logger;
        _receiver = receiver;
    }

    public override Task<Reply> Publish(PublishRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Published message from gRPC. Topic: {request.Topic}");
        _receiver.Publish(new Message
        {
            Topic = request.Topic,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow
        });
        
        return Task.FromResult(new Reply
        {
            Success = true,
        });
    }
}