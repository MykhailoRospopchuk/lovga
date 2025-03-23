namespace LovgaBroker.Services.BackgroundServices;

using Models;
using Services;

public class BrokerWorker : BackgroundService
{
    private readonly IMessageBroker _broker;
    private readonly ILogger<BrokerWorker> _logger;

    public BrokerWorker(IMessageBroker broker, ILogger<BrokerWorker> logger)
    {
        _broker = broker;
        _logger = logger;

        // Example subscription
        _broker.Subscribe("test-topic", HandleMessageAsync);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Broker Service is starting.");

        if (_broker is MessageBroker messageBroker)
        {
            await messageBroker.DispatchAsync(stoppingToken);
        }
    }

    private Task HandleMessageAsync(Message message)
    {
        _logger.LogInformation($"Received message: {message.Content} at {message.CreatedAt}");
        return Task.CompletedTask;
    }
}