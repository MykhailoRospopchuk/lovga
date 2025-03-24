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
            _broker.Publish(new Message
            {
                Topic = "bobr-topic",
                Content = $"from bobr - {DateTime.Now.ToLongTimeString()}",
            });

            await Task.Delay(500, cancellationToken);
        }
    }
}
