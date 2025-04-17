namespace LovgaSatellite.GrpcClientServices;

using API;
using GrpcChannel;
using LovgaCommon;

public class SubscriberGrpcClient
{
    private static readonly string SubscriberId = Guid.NewGuid().ToString();
    private static readonly HashSet<string> ActiveTopics = new ();
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

        if (ActiveTopics.Contains(topic))
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
            Id = SubscriberId
        });

        if (reply.Success)
        {
            ActiveTopics.Add(topic);
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
            Id = SubscriberId
        });

        if (reply.Success)
        {
            ActiveTopics.Remove(topic);
        }

        return reply.Success;
    }

    public string[] GetActiveTopics()
    {
        return ActiveTopics.ToArray();
    }
}