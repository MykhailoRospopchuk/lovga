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

        await _receiver.DispatchAsync(cancellationToken);
    }
}