namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Grpc.Core;
using Interfaces;

public class ChannelManger : IChannelManger
{
    private readonly ConcurrentDictionary<(string, int), Channel> _channels = new();

    public Channel? GetChannel(string host, int port)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (port < 0)
        {
            throw new ArgumentException("Port cannot be negative", nameof(port));
        }

        if (_channels.TryGetValue((host, port), out Channel? channel))
        {
            return channel;
        }

        channel = new Channel(host, port, ChannelCredentials.Insecure);

        if (_channels.TryAdd((host, port), channel))
        {
            return channel;
        }
        return null;
    }

    // TODO: When???? when I need to remove channel?
    public bool RemoveChannel(string host, int port)
    {
        return _channels.TryRemove((host, port), out _);
    }
}