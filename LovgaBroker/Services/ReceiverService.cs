namespace LovgaBroker.Services;

using System.Threading.Channels;
using Interfaces;
using Models;

public class ReceiverService : IReceiver
{
    private readonly Channel<Message> _messages = Channel.CreateUnbounded<Message>();
    private readonly IBrokerManager _brokerManager;
    private readonly ILogger<ReceiverService> _logger;
    private readonly StorageService _storageService;

    public ReceiverService(
        IBrokerManager brokerManager,
        ILogger<ReceiverService> logger,
        StorageService storageService)
    {
        _brokerManager = brokerManager;
        _logger = logger;
        _storageService = storageService;
    }

    public async ValueTask Publish(Message message)
    {
        var id = await _storageService.InsertMessage(message);
        message.SetId(id);

        await _messages.Writer.WriteAsync(message);
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            var broker = _brokerManager.GetBroker(message.Topic);
            await broker.EnqueueMessage(message);
        }
    }

    public async Task LoadStoredMessages()
    {
        var chunk = _storageService.GetChunkMessagesAsync();

        await foreach (var message in chunk)
        {
            await _messages.Writer.WriteAsync(message);
        }

        _logger.LogInformation("Loading stored messages completed");
    }
}