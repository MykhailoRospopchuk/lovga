namespace LovgaSatellite.GrpcClientServices;

using System.Text.Json;
using Grpc.Net.Client;
using GrpcChannel;
using LovgaCommon;

public sealed class PublisherGrpcClient
{
    private readonly GrpcChannel? _channel = GrpcChannelProvider.GetInstance()?.Channel;

    public async Task<bool> PublishMessage<T>(T message, string topic, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Channel is not initialized");
        }

        ArgumentException.ThrowIfNullOrEmpty(topic);

        var content = message is string rawString
            ? rawString
            : JsonSerializer.Serialize(message);

        var client = new Publisher.PublisherClient(_channel);

        var reply = await client.PublishAsync(
            new PublishRequest
            {
                Topic = topic,
                Content = content,
            },
            cancellationToken: cancellationToken);

        return reply.Success;
    }
}