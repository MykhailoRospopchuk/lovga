namespace LovgaSatellite.API;

using ActionHolder;
using GrpcChannel;
using Models;

public static class SatelliteExtensions
{
    private static string _channelHost = string.Empty;
    private static int _channelPort;
    private static bool _configuredChannel;

    private static string _satelliteHost = string.Empty;
    private static int _satellitePort;

    internal static bool ConfiguredHost { get; private set; }

    public static void ConfigureChannel(string host, int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (_configuredChannel)
        {
            throw new InvalidOperationException("ConfigureChannel can only be called once");
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }

        _channelHost = host;
        _channelPort = port;

        _configuredChannel = true;
    }

    [Obsolete("Bi-directional streaming no longer requires hosting a local gRPC server on the satellite.")]
    public static void ConfigureHost(string host, int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (ConfiguredHost)
        {
            throw new InvalidOperationException("ConfigureHost can only be called once");
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
        }

        _satelliteHost = host;
        _satellitePort = port;

        ConfiguredHost = true;
    }

    [Obsolete("Bi-directional streaming no longer requires hosting a local gRPC server on the satellite.")]
    public static (string host, int port) GetHostConfiguration()
    {
        if (!ConfiguredHost)
        {
            throw new InvalidOperationException("Host is not configured");
        }

        return (_satelliteHost, _satellitePort);
    }

    public static bool InitChannel()
    {
        if (!_configuredChannel)
        {
            throw new NotSupportedException("You must call ConfigureChannel() before calling InitChannel()");
        }

        var instance = GrpcChannelProvider.GetInstance((_channelHost, _channelPort));

        return instance is not null;
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