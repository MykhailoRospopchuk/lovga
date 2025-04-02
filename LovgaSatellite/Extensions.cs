namespace LovgaSatellite;

using GrpcChannel;

public static class Extensions
{
    public static bool InitChannel(string host, int port)
    {
        var instance = GrpcChannelProvider.GetInstance((host, port));

        return instance is null;
    }
}