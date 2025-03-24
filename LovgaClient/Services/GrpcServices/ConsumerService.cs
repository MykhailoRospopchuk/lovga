namespace LovgaClient.Services.GrpcServices;

using Grpc.Core;
using LovgaCommon;

public class ConsumerService : Consumer.ConsumerBase
{
    public override Task<Reply> Notify(NotifyRequest request, ServerCallContext context)
    {
        Console.WriteLine($"Message from Broker: {request.Test}");

        return Task.FromResult(new Reply
        {
            Success = true
        });
    }
}