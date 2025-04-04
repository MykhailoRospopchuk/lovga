namespace LovgaSatellite.GrpcChannel;

using Grpc.Core;

internal class GrpcChannelProvider
{
    private GrpcChannelProvider() { }

    private static GrpcChannelProvider? _instance;

    private static readonly Lock Lock = new ();

    public static GrpcChannelProvider? GetInstance((string host, int port)? data = null)
    {
        if (_instance == null)
        {
            lock (Lock)
            {
                if (_instance == null &&  data.HasValue && !string.IsNullOrEmpty(data.Value.host))
                {
                    _instance = new GrpcChannelProvider();
                    _instance.Channel = new Channel(data.Value.host, data.Value.port, ChannelCredentials.Insecure);
                }
            }
        }
        return _instance;
    }

    public Channel? Channel { get; set; }
}