namespace LovgaClient.Services.GrpcServices;

using Grpc.Core;
using LovgaCommon;

public class ConsumerService : ConsumerClient.ConsumerClientBase
{
    public override Task<Reply> NotifyConsumer(NotifyRequest request, ServerCallContext context)
    {
        Console.WriteLine($"Message from Broker: {request.Test}");

        return Task.FromResult(new Reply
        {
            Success = true
        });
    }
}