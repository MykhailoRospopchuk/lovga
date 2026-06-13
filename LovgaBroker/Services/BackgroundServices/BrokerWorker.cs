namespace LovgaBroker.Services.BackgroundServices;

using System.Collections.Concurrent;
using Interfaces;
using Services;

public class BrokerWorker : BackgroundService
{
    private readonly IBrokerManager _brokerManager;
    private readonly ILogger<BrokerWorker> _logger;
    private readonly ConcurrentDictionary<string, Task> _dispatchers = new();
    private CancellationToken _cancellationToken = CancellationToken.None;

    public BrokerWorker(IBrokerManager brokerManager, ILogger<BrokerWorker> logger)
    {
        _brokerManager = brokerManager;
        _logger = logger;

        _brokerManager.OnBrokerAdded += OnBrokerAdded;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
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

        // Use the stored cancellation token so graceful shutdown also stops dynamically-added brokers.
        _dispatchers.TryAdd(broker.Topic, broker.DispatchAsync(_cancellationToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // WaitAsync respects the host's graceful shutdown deadline (default 5s).
            // Without this, a stuck dispatcher (e.g. awaiting _subscriberSignal or a hung gRPC call)
            // would prevent the process from ever exiting cleanly.
            await Task.WhenAll(_dispatchers.Values).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("BrokerWorker forced shutdown — some dispatchers were still running");
        }

        _logger.LogInformation("Broker Worker is stopping");
    }
}