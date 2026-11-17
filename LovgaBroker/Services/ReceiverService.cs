namespace LovgaBroker.Services;

using System.Threading.Channels;
using Interfaces;
using Models;

public sealed class ReceiverService(
    IBrokerManager brokerManager,
    ILogger<ReceiverService> logger,
    IStorageService storageService) : IReceiver
{
    // Bounded channel: when full, WriteAsync yields — naturally propagating back-pressure to publishers.
    // FullMode.Wait means the gRPC publish handler will pause (not drop or throw) until space is available.
    // SingleReader=true allows the channel to skip reader-count tracking for a perf gain.
    private readonly Channel<Message> _messages = Channel.CreateBounded<Message>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    public async ValueTask<bool> Publish(Message message, CancellationToken cancellationToken = default)
    {
        var id = await storageService.InsertMessage(message);
        if (id <= 0)
        {
            logger.LogError("Dropping publish for topic {Topic} because SQLite insert failed", message.Topic);
            return false;
        }

        message.SetId(id);
        await _messages.Writer.WriteAsync(message, cancellationToken);
        return true;
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            var broker = brokerManager.GetBroker(message.Topic);
            await broker.EnqueueMessage(message);
        }
    }

    public async Task LoadStoredMessages(CancellationToken cancellationToken = default)
    {
        var maxId = await storageService.GetMaxMessageIdAsync();
        if (maxId <= 0)
        {
            logger.LogInformation("No stored messages to recover on startup");
            return;
        }

        var recoveredCount = 0;
        await foreach (var message in storageService.GetChunkMessagesAsync(maxId, cancellationToken))
        {
            await _messages.Writer.WriteAsync(message, cancellationToken);
            recoveredCount++;
        }

        logger.LogInformation("Loading stored messages completed ({Count} messages recovered up to Id {MaxId})", recoveredCount, maxId);
    }
}