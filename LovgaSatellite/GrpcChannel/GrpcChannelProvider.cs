namespace LovgaSatellite.GrpcChannel;

using System.Net.Http;
using Grpc.Net.Client;

internal sealed class GrpcChannelProvider
{
    private GrpcChannelProvider() { }

    private static GrpcChannelProvider? _instance;

    private static readonly Lock Lock = new();

    public static GrpcChannelProvider? GetInstance((string host, int port)? data = null)
    {
        if (_instance is null)
        {
            lock (Lock)
            {
                if (_instance is null && data.HasValue && !string.IsNullOrEmpty(data.Value.host))
                {
                    var handler = new SocketsHttpHandler
                    {
                        EnableMultipleHttp2Connections = true,
                        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                        KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan
                    };

                    var address = data.Value.host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                  data.Value.host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        ? $"{data.Value.host}:{data.Value.port}"
                        : $"http://{data.Value.host}:{data.Value.port}";

                    _instance = new GrpcChannelProvider
                    {
                        Channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
                        {
                            HttpHandler = handler
                        })
                    };
                }
            }
        }
        return _instance;
    }

    public void ThrowIfChannelNull()
    {
        if (_instance?.Channel is null)
        {
            throw new InvalidOperationException("Channel is not initialized");
        }
    }

    public GrpcChannel? Channel { get; set; }
}