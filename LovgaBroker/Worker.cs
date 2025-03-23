namespace LovgaBroker;

using Models;
using Services;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IMessageBroker _broker;

    public Worker(ILogger<Worker> logger, IMessageBroker broker)
    {
        _logger = logger;
        _broker = broker;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            _broker.Publish(new Message
            {
                Topic = "test-topic",
                Content = "big bobr",
            });

            await Task.Delay(1000, cancellationToken);
        }
    }
}
