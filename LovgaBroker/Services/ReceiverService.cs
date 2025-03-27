namespace LovgaBroker.Services;

using System.Threading.Channels;
using Interfaces;
using Models;

public class ReceiverService : IReceiver
{
    private readonly Channel<Message> _messages = Channel.CreateUnbounded<Message>();
    private readonly IBrokerManager _brokerManager;
    private readonly ILogger<ReceiverService> _logger;

    public ReceiverService(IBrokerManager brokerManager, ILogger<ReceiverService> logger)
    {
        _brokerManager = brokerManager;
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
            var broker = _brokerManager.GetBroker(message.Topic);
            await broker.EnqueueMessage(message);
        }
    }
}