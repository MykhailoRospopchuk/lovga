namespace LovgaBroker;

using Interfaces;
using Models;
using Services;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IBrokerManager _broker;

    public Worker(ILogger<Worker> logger, IBrokerManager broker)
    {
        _logger = logger;
        _broker = broker;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var broker = _broker.GetBroker("bobr-topic");
        while (!cancellationToken.IsCancellationRequested)
        {

            await broker.EnqueueMessage(new Message
            {
                Topic = "bobr-topic",
                Content = $"from bobr - {DateTime.Now.ToLongTimeString()}",
            });

            await Task.Delay(500, cancellationToken);
        }
    }
}
