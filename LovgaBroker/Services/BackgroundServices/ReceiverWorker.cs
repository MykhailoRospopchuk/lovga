namespace LovgaBroker.Services.BackgroundServices;

using Interfaces;

public sealed class ReceiverWorker(ILogger<ReceiverWorker> logger, IReceiver receiver) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Receiver Worker is starting.");

        // Load stored messages up to the startup watermark concurrently so large recovery sets don't block the bounded channel
        _ = Task.Run(async () =>
        {
            try
            {
                await receiver.LoadStoredMessages(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading stored messages on startup");
            }
        }, cancellationToken);

        await receiver.DispatchAsync(cancellationToken);
    }
}