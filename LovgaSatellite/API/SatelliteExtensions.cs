namespace LovgaSatellite.API;

using ActionHolder;
using GrpcChannel;
using Models;

public static class SatelliteExtensions
{
    private static string _channelHost = string.Empty;
    private static int _channelPort;
    private static bool _configuredChannel;

    private static string _host = string.Empty;
    private static int _port;

    internal static bool ConfiguredHost { get; private set; }

    public static void ConfigureChannel(string host, int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (_configuredChannel)
        {
            throw new InvalidOperationException("ConfigureChannel can only be called once");
        }

        if (port < 0)
        {
            throw new ArgumentException("Port cannot be negative", nameof(port));
        }

        _channelHost = host;
        _channelPort = port;

        _configuredChannel = true;
    }

    public static void ConfigureHost(string host, int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (ConfiguredHost)
        {
            throw new InvalidOperationException("ConfigureHost can only be called once");
        }

        if (port < 0)
        {
            throw new ArgumentException("Port cannot be negative", nameof(port));
        }

        _host = host;
        _port = port;

        ConfiguredHost = true;
    }

    public static (string host, int port) GetHostConfiguration()
    {
        if (!ConfiguredHost)
        {
            throw new InvalidOperationException("Host is not configured");
        }

        return (_host, _port);
    }
    public static bool InitChannel()
    {
        if (!_configuredChannel)
        {
            throw new NotSupportedException("You must call ConfigureHost() before calling InitChannel()");
        }

        var instance = GrpcChannelProvider.GetInstance((_channelHost, _channelPort));

        return instance is null;
    }

    public static bool RegisterAction(string topic, Action<ActionModel> action)
    {
        return ActionHolder.AddAction(topic, action);
    }

    public static bool RemoveAction(string topic)
    {
        return ActionHolder.RemoveAction(topic);
    }
}