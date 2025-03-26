namespace LovgaBroker.Services.BackgroundServices;

using System.Collections.Concurrent;
using Interfaces;
using Services;

public class BrokerWorker : BackgroundService
{
    private readonly IBrokerManager _brokerManager;
    private readonly ILogger<BrokerWorker> _logger;
    private readonly ConcurrentDictionary<string, Task> _dispatchers = new();

    public BrokerWorker(IBrokerManager brokerManager, ILogger<BrokerWorker> logger)
    {
        _brokerManager = brokerManager;
        _logger = logger;

        _brokerManager.OnBrokerAdded += OnBrokerAdded;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broker Worker is starting");

        var brokers = _brokerManager.GetAllBrokers();
        foreach (var broker in brokers)
        {
            if (broker is MessageBroker messageBroker)
            {
                _dispatchers.TryAdd(messageBroker.Topic, messageBroker.DispatchAsync(cancellationToken));
            }
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private void OnBrokerAdded(IMessageBroker broker)
    {
        if (_dispatchers.Keys.Contains(broker.Topic))
        {
            return;
        }

        _dispatchers.TryAdd(broker.Topic, broker.DispatchAsync(CancellationToken.None));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(_dispatchers.Values);
        _logger.LogInformation("Broker Worker is stopping");
    }
}