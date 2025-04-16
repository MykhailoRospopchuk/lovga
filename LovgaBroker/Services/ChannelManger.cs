namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Grpc.Core;
using Interfaces;

public class ChannelManger : IChannelManger
{
    private readonly ConcurrentDictionary<string, (Channel channel, List<string> consumerId)> _channels = new();
    public Channel? GetChannel(string target)
    {
        if (_channels.TryGetValue(target, out var holder))
        {
            return holder.channel;
        }

        holder = (new Channel(target, ChannelCredentials.Insecure), new List<string>());

        if (_channels.TryAdd(target, holder))
        {
            return holder.channel;
        }
        return null;
    }

    public void RegisterConsumer(string target, string consumerId)
    {
        if (_channels.TryGetValue(target, out var holder))
        {
            holder.consumerId.Add(consumerId);
        }
    }

    public void UnregisterConsumer(string target, string consumerId)
    {
        if (_channels.TryGetValue(target, out var holder))
        {
            holder.consumerId.RemoveAll(id => id == consumerId);

            if (holder.consumerId.Count == 0)
            {
                ShutDownChannel(target);
            }
        }
    }

    public bool ChannelExists(string target)
    {
        if (_channels.TryGetValue(target, out var holder))
        {
            return holder.channel.State != ChannelState.Shutdown;
        }
        return false;
    }

    private void ShutDownChannel(string target)
    {
        if (_channels.TryRemove(target, out var holder))
        {
            holder.channel.ShutdownAsync().GetAwaiter().GetResult();
        }
    }
}