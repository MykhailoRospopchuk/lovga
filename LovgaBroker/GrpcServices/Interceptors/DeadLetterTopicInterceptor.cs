namespace LovgaBroker.GrpcServices.Interceptors;

using Grpc.Core;
using Grpc.Core.Interceptors;
using LovgaCommon;
using LovgaCommon.Constants;

public class DeadLetterTopicInterceptor : Interceptor
{
    private readonly ILogger<DeadLetterTopicInterceptor> _logger;

    public DeadLetterTopicInterceptor(ILogger<DeadLetterTopicInterceptor> logger)
    {
        _logger = logger;
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {

        if (request is PublishRequest publishRequest && publishRequest.Topic == QueueTopic.DeadLetterQueue)
        {
            _logger.LogWarning("Request aborted: Topic is DeadLetterQueue.");

            throw new RpcException(new Status(StatusCode.Unknown, "Dead Letter Queue is reserved queue."));
        }

        return base.UnaryServerHandler(request, context, continuation);
    }
}