namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Interfaces;

public class BrokerManager : IBrokerManager
{
    private readonly ConcurrentDictionary<string, IMessageBroker> _brokers = new();

    public event Action<IMessageBroker>? OnBrokerAdded;

    public IMessageBroker GetBroker(string topic)
    {
        if (_brokers.TryGetValue(topic, out IMessageBroker? broker))
        {
            return broker;
        }

        broker = new MessageBroker(topic);
        if (_brokers.TryAdd(topic, broker))
        {
            OnBrokerAdded?.Invoke(broker);
        }

        return broker;
    }

    public IEnumerable<IMessageBroker> GetAllBrokers()
    {
        return _brokers.Values;
    }
}