namespace LovgaBroker.Services.BackgroundServices;

using Interfaces;

public class ReceiverWorker : BackgroundService
{
    private readonly ILogger<ReceiverWorker> _logger;
    private readonly IReceiver _receiver;

    public ReceiverWorker(ILogger<ReceiverWorker> logger, IReceiver receiver)
    {
        _logger = logger;
        _receiver = receiver;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Receiver Worker is starting.");

        // Load stored messages concurrently in the background so it doesn't deadlock the dispatch loop
        _ = Task.Run(async () =>
        {
            try
            {
                await _receiver.LoadStoredMessages();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading stored messages on startup");
            }
        }, cancellationToken);

        await _receiver.DispatchAsync(cancellationToken);
    }
}