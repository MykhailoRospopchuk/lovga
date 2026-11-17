namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using LovgaBroker.Interfaces;
using LovgaCommon;
using Models;

public sealed class PublisherGrpcServer(
    ILogger<PublisherGrpcServer> logger,
    IReceiver receiver) : Publisher.PublisherBase
{
    public override async Task<Reply> Publish(PublishRequest request, ServerCallContext context)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Published message from gRPC. Topic: {Topic}", request.Topic);
        }

        var success = await receiver.Publish(new Message
        {
            Topic = request.Topic,
            Content = request.Content
        }, context.CancellationToken);

        return new Reply
        {
            Success = success,
        };
    }
}