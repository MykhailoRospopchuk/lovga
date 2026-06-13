namespace LovgaSatellite.GrpcClientServices;

using System.Collections.Concurrent;
using API;
using GrpcChannel;
using LovgaCommon;

public class SubscriberGrpcClient
{
    // Instance-level ID: different DI-resolved instances get independent subscriber identities.
    // Previously static, meaning ALL instances in the process shared one ID — wrong for multi-scope use.
    private readonly string _subscriberId = Guid.NewGuid().ToString();

    // ConcurrentDictionary used as a thread-safe set (byte value is unused).
    // Previously HashSet<string> — not thread-safe, corrupts under concurrent Subscribe/Unsubscribe calls.
    private readonly ConcurrentDictionary<string, byte> _activeTopics = new();

    private readonly GrpcChannelProvider? _channelProvider;

    public SubscriberGrpcClient()
    {
        _channelProvider = GrpcChannelProvider.GetInstance();
    }

    public async Task<bool> Subscribe(string topic)
    {
        if (_channelProvider is null)
        {
            throw new InvalidOperationException("Channel Provider is not initialized");
        }

        if (string.IsNullOrEmpty(topic))
        {
            throw new ArgumentNullException(nameof(topic));
        }

        if (_activeTopics.ContainsKey(topic))
        {
            throw new InvalidOperationException("Already subscribed");
        }

        _channelProvider.ThrowIfChannelNull();

        var client = new Subscriber.SubscriberClient(_channelProvider.Channel);

        var hostConfig = SatelliteExtensions.GetHostConfiguration();

        var reply = await client.SubscribeAsync(new SubscribeRequest
        {
            Host = hostConfig.host,
            Port = hostConfig.port,
            Topic = topic,
            Id = _subscriberId
        });

        if (reply.Success)
        {
            _activeTopics.TryAdd(topic, 0);
        }

        return reply.Success;
    }

    public async Task<bool> Unsubscribe(string topic)
    {
        if (_channelProvider is null)
        {
            throw new InvalidOperationException("Channel is not initialized");
        }

        if (string.IsNullOrEmpty(topic))
        {
            throw new ArgumentNullException(nameof(topic));
        }

        _channelProvider.ThrowIfChannelNull();

        var client = new Subscriber.SubscriberClient(_channelProvider.Channel);

        var reply = await client.UnSubscribeAsync(new UnsubscribeRequest
        {
            Topic = topic,
            Id = _subscriberId
        });

        if (reply.Success)
        {
            _activeTopics.TryRemove(topic, out _);
        }

        return reply.Success;
    }

    public string[] GetActiveTopics()
    {
        return _activeTopics.Keys.ToArray();
    }
}