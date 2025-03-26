namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Interfaces;

public class BrokerManager : IBrokerManager
{
    private readonly ConcurrentDictionary<string, IMessageBroker> _brokers = new();
    
    public IMessageBroker GetBroker(string topic)
    {
        return _brokers.GetOrAdd(topic, t => new MessageBroker(t));
    }

    public IEnumerable<IMessageBroker> GetAllBrokers()
    {
        return _brokers.Values;
    }
}