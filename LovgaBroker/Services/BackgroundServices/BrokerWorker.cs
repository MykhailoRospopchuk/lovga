namespace LovgaBroker.Services.BackgroundServices;

using System.Collections.Concurrent;
using Interfaces;

public sealed class BrokerWorker(IBrokerManager brokerManager, ILogger<BrokerWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, Task> _dispatchers = new();
    private CancellationToken _cancellationToken = CancellationToken.None;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        brokerManager.OnBrokerAdded += OnBrokerAdded;

        logger.LogInformation("Broker Worker is starting");

        foreach (var broker in brokerManager.GetAllBrokers())
        {
            OnBrokerAdded(broker);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            brokerManager.OnBrokerAdded -= OnBrokerAdded;
        }
    }

    private void OnBrokerAdded(IMessageBroker broker)
    {
        _dispatchers.GetOrAdd(broker.Topic, _ => broker.DispatchAsync(_cancellationToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        try
        {
            await Task.WhenAll(_dispatchers.Values).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("BrokerWorker forced shutdown — some dispatchers were still running");
        }

        logger.LogInformation("Broker Worker is stopping");
    }
}