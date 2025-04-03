namespace LovgaSatellite.GrpcClientServices;

using System.Text.Json;
using Grpc.Core;
using GrpcChannel;
using LovgaCommon;

public class PublisherGrpcClient
{
    private readonly Channel? _channel;

    public PublisherGrpcClient()
    {
        _channel = GrpcChannelProvider.GetInstance()?.Channel;
    }

    public async Task<bool> PublishMessage<T>(T message, string topic) 
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Channel is not initialized");
        }

        if (string.IsNullOrEmpty(topic))
        {
            throw new ArgumentNullException(nameof(topic));
        }

        var content = JsonSerializer.Serialize(message);

        var client = new Publisher.PublisherClient(_channel);

        var reply = await client.PublishAsync(new PublishRequest()
        {
            Topic = topic,
            Content = content,
        });

        return reply.Success;
    }
}