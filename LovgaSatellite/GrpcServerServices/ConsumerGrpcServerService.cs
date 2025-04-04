namespace LovgaSatellite.GrpcServerServices;

using ActionHolder;
using Grpc.Core;
using LovgaCommon;
using Models;

public class ConsumerGrpcServerService : Consumer.ConsumerBase
{
    public override Task<Reply> Notify(NotifyRequest request, ServerCallContext context)
    {
        var action = ActionHolder.GetAction(request.Topic);

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