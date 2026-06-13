namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Grpc.Core;
using Interfaces;

public class ChannelManger : IChannelManger
{
    // Value: (gRPC channel, set of consumer IDs currently using this channel)
    // ConcurrentDictionary<string, byte> gives lock-free set semantics — List<string> was NOT thread-safe.
    private readonly ConcurrentDictionary<string, (Channel channel, ConcurrentDictionary<string, byte> consumerIds)> _channels = new();

    public Channel? GetChannel(string target)
    {
        // GetOrAdd is atomic: the loser thread always gets the winner's channel, never null.
        // Previously TryGetValue + TryAdd had a TOCTOU window where the losing thread
        // returned null AND leaked the Grpc.Core.Channel it created.
        return _channels.GetOrAdd(target,
            t => (new Channel(t, ChannelCredentials.Insecure), new ConcurrentDictionary<string, byte>()))
            .channel;
    }

    public void RegisterConsumer(string target, string consumerId)
    {
        // GetOrAdd ensures the entry exists even if RegisterConsumer races with GetChannel.
        var holder = _channels.GetOrAdd(target,
            t => (new Channel(t, ChannelCredentials.Insecure), new ConcurrentDictionary<string, byte>()));

        holder.consumerIds.TryAdd(consumerId, 0);
    }

    public void UnregisterConsumer(string target, string consumerId)
    {
        if (!_channels.TryGetValue(target, out var holder))
            return;

        holder.consumerIds.TryRemove(consumerId, out _);

        // Only shut down when the last consumer unregisters.
        // ConcurrentDictionary.IsEmpty is a safe, lock-free check.
        if (holder.consumerIds.IsEmpty)
        {
            ShutDownChannel(target);
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
            // Fire-and-forget on the thread pool — avoids blocking the gRPC handler thread.
            // ShutdownAsync is a graceful shutdown; the caller does not need to await it.
            _ = Task.Run(() => holder.channel.ShutdownAsync());
        }
    }
}