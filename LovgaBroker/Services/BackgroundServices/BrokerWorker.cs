namespace LovgaBroker.Services.BackgroundServices;

using Interfaces;
using Services;

public class BrokerWorker : BackgroundService
{
    private readonly IBrokerManager _broker;
    private readonly ILogger<BrokerWorker> _logger;

    public BrokerWorker(IBrokerManager broker, ILogger<BrokerWorker> logger)
    {
        _broker = broker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broker Service is starting.");

        var brokers = _broker.GetAllBrokers();
        var dispatchTasks = brokers.Select(b =>
        {
            if (b is MessageBroker messageBroker)
            {
                return messageBroker.DispatchAsync(cancellationToken);
            }
            
            return Task.CompletedTask;
        });

        await Task.WhenAll(dispatchTasks);
    }
}