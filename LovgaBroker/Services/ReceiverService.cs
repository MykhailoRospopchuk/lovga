namespace LovgaBroker.Services;

using System.Threading.Channels;
using Interfaces;
using Models;

public class ReceiverService : IReceiver
{
    // Bounded channel: when full, WriteAsync yields — naturally propagating back-pressure to publishers.
    // FullMode.Wait means the gRPC publish handler will pause (not drop or throw) until space is available.
    // Queue for messages waiting to be saved to DB
    private readonly Channel<Message> _pendingSaves = Channel.CreateBounded<Message>(
        new BoundedChannelOptions(20000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly IBrokerManager _brokerManager;
    private readonly ILogger<ReceiverService> _logger;
    private readonly IStorageService _storageService;

    public ReceiverService(
        IBrokerManager brokerManager,
        ILogger<ReceiverService> logger,
        IStorageService storageService)
    {
        _brokerManager = brokerManager;
        _logger = logger;
        _storageService = storageService;

        _ = Task.Run(BatchInsertLoopAsync);
    }

    public async ValueTask Publish(Message message)
    {
        // Enqueue for batched DB insertion. 
        // We return immediately to the publisher to keep gRPC latency low.
        await _pendingSaves.Writer.WriteAsync(message);
    }

    private async Task BatchInsertLoopAsync()
    {
        var batch = new List<Message>(1000);
        var reader = _pendingSaves.Reader;

        while (await reader.WaitToReadAsync())
        {
            batch.Clear();

            while (batch.Count < 1000 && reader.TryRead(out var msg))
            {
                batch.Add(msg);
            }

            if (batch.Count > 0)
            {
                try
                {
                    // 1. Batch insert into SQLite (which also populates msg.Id)
                    await _storageService.InsertMessagesAsync(batch);

                    // 2. Dispatch the saved messages to the Broker so it can deliver them
                    foreach (var message in batch)
                    {
                        var broker = _brokerManager.GetBroker(message.Topic);
                        await broker.EnqueueMessage(message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to batch insert messages in ReceiverService");
                }
            }
        }
    }

    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        // Now DispatchAsync is no longer needed in ReceiverService, because we dispatch directly from the batch loop.
        // But to satisfy IReceiver, we just keep it alive.
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}