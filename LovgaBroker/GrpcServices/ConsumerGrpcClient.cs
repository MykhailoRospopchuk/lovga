namespace LovgaBroker.GrpcServices;

using Grpc.Core;
using Interfaces;
using LovgaCommon;
using Models;

public class ConsumerGrpcClient : IConsumerGrpcClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _topic;
    private readonly ILogger<ConsumerGrpcClient> _logger;
    private Channel _channel;

    public ConsumerGrpcClient(string host, int port, string topic, ILogger<ConsumerGrpcClient> logger)
    {
        _host = host;
        _port = port;
        _topic = topic;
        _logger = logger;
    }

    public bool InitChannel()
    {
        try
        {
            _channel = new Channel(_host, _port, ChannelCredentials.Insecure);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error initializing channel");
            return false;
        }

        return true;
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

        var client = new Consumer.ConsumerClient(_channel);

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