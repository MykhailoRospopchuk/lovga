namespace LovgaSatellite.API;

using ActionHolder;
using GrpcChannel;
using Models;

public static class Extensions
{
    public static bool InitChannel(string host, int port)
    {
        var instance = GrpcChannelProvider.GetInstance((host, port));

        return instance is null;
    }

    public static bool RegisterAction(string topic, Action<ActionModel> action)
    {
        return ActionHolder.AddAction(topic, action);
    }
}