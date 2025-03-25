namespace LovgaBroker.Services.GrpcServices;

using Grpc.Core;
using Interfaces;
using LovgaCommon;
using Models;

public class ConsumerService : IConsumerObserver
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _topic;
    private readonly ILogger<ConsumerService> _logger;

    public ConsumerService(string host, int port, string topic, ILogger<ConsumerService> logger)
    {
        _host = host;
        _port = port;
        _topic = topic;
        _logger = logger;
    }

    // TODO: handle when consumer unpredicted shut down
    public async Task DeliverMessage(Message message) 
    {
        if (message.Topic != _topic)
        {
            // TODO: need figure out how handle this case
            _logger.LogError($"Invalid topic {_topic}");
            throw new ArgumentException($"Invalid topic {_topic}");
        }

        var channel = new Channel(_host, _port, ChannelCredentials.Insecure);
        var client = new Consumer.ConsumerClient(channel);

        var reply = await client.NotifyAsync(new NotifyRequest
        {
            Content = message.Content,
        });

        if (!reply.Success)
        {
            _logger.LogError("Error. Consumer failed to notify");
        }
    }
}