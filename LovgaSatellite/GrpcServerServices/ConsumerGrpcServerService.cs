namespace LovgaSatellite.GrpcServerServices;

using ActionHolder;
using Grpc.Core;
using LovgaCommon;
using Models;

public class ConsumerGrpcServerService : Consumer.ConsumerBase
{
    public override Task<Reply> Notify(NotifyRequest request, ServerCallContext context)
    {
        Console.WriteLine($"Message from Broker: Topic-{request.Topic} Content-{request.Content}");

        var action = ActionHolder.GetAction(request.Topic);

        if (action is null)
        {
            Console.WriteLine($"There is no action for Topic: {request.Topic}");
        }

        action?.Invoke(new ActionModel
        {
            Content = request.Content
        });

        return Task.FromResult(new Reply
        {
            Success = true
        });
    }
}