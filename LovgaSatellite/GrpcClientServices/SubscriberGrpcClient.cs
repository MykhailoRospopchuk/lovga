namespace LovgaSatellite.GrpcClientServices;

using Grpc.Core;
using GrpcChannel;
using LovgaCommon;

public class SubscriberGrpcClient
{
    private readonly string _subscriberId = Guid.NewGuid().ToString();
    private readonly Channel? _channel;

    public SubscriberGrpcClient()
    {
        _channel = GrpcChannelProvider.GetInstance()?.Channel;
    }

    public async Task<bool> Subscribe(string topic)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Channel is not initialized");
        }

        if (string.IsNullOrEmpty(topic))
        {
            throw new ArgumentNullException(nameof(topic));
        }

        var client = new Subscriber.SubscriberClient(_channel);

        var reply = await client.SubscribeAsync(new SubscribeRequest
        {
            Host = "localhost",
            Port = 7880,
            Topic = topic,
            Id = _subscriberId
        });

        return reply.Success;
    }
}