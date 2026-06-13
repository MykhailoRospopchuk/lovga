namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Interfaces;

public class BrokerManager : IBrokerManager
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ConcurrentDictionary<string, IMessageBroker> _brokers = new();

    public BrokerManager(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public event Action<IMessageBroker>? OnBrokerAdded;

    public IMessageBroker GetBroker(string topic)
    {
        return _brokers.GetOrAdd(topic, t =>
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var broker = scope.ServiceProvider.GetRequiredService<IMessageBroker>();
            broker.SetTopic(t);
            OnBrokerAdded?.Invoke(broker);
            return broker;
        });
    }

    public IEnumerable<IMessageBroker> GetAllBrokers()
    {
        return _brokers.Values;
    }
}