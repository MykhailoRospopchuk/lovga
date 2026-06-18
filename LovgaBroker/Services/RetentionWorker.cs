namespace LovgaBroker.Services;

using Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class RetentionWorker : BackgroundService
{
    private readonly IStorageService _storageService;
    private readonly ILogger<RetentionWorker> _logger;

    public RetentionWorker(IStorageService storageService, ILogger<RetentionWorker> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RetentionWorker is starting.");

        // Run every 1 hour
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var threshold = DateTime.UtcNow.AddDays(-7);
                _logger.LogInformation("Running message retention cleanup. Deleting messages older than {Threshold}", threshold);

                await _storageService.DeleteMessagesOlderThanAsync(threshold);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RetentionWorker is stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in RetentionWorker");
        }
    }
}
