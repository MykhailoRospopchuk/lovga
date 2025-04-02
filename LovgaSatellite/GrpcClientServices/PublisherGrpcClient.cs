namespace LovgaSatellite.GrpcClientServices;

using System.Text.Json;
using Grpc.Core;
using GrpcChannel;
using LovgaCommon;

public class PublisherGrpcClient : IDisposable
{
    private bool _disposed;
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

        try
        {
            var content = JsonSerializer.Serialize(message);

            var client = new Publisher.PublisherClient(_channel);

            var reply = await client.PublishAsync(new PublishRequest()
            {
                Topic = topic,
                Content = content,
            });

            return reply.Success;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (_channel != null)
            {
                _channel.ShutdownAsync().GetAwaiter().GetResult();
            }
        }

        _disposed = true;
    }
}