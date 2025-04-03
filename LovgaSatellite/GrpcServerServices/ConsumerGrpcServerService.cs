namespace LovgaSatellite.GrpcServerServices;

using Grpc.Core;
using LovgaCommon;

public class ConsumerGrpcServerService : Consumer.ConsumerBase
{
    
    public override Task<Reply> Notify(NotifyRequest request, ServerCallContext context)
    {
        Console.WriteLine($"Message from Broker: {request.Content}");

        return Task.FromResult(new Reply
        {
            Success = true
        });
    }
}