namespace LovgaBroker.Services;

using System.Threading.Channels;
using Interfaces;
using Models;

public class ReceiverService : IReceiver
{
    private readonly Channel<Message> _messages = Channel.CreateUnbounded<Message>();
    private readonly IBrokerManager _broker;
    private readonly ILogger<ReceiverService> _logger;

    public ReceiverService(IBrokerManager broker, ILogger<ReceiverService> logger)
    {
        _broker = broker;
        _logger = logger;
    }

    public ValueTask Publish(Message message)
    {
        return _messages.Writer.WriteAsync(message);
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            var broker = _broker.GetBroker(message.Topic);
            await broker.EnqueueMessage(message);
        }
    }
}