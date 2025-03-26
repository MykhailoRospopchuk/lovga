namespace LovgaBroker.Services.BackgroundServices;

using Services;

public class BrokerWorker : BackgroundService
{
    private readonly IMessageBroker _broker;
    private readonly ILogger<BrokerWorker> _logger;

    public BrokerWorker(IMessageBroker broker, ILogger<BrokerWorker> logger)
    {
        _broker = broker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Broker Service is starting.");

        if (_broker is MessageBroker messageBroker)
        {
            await messageBroker.DispatchAsync(stoppingToken);
        }
    }
}