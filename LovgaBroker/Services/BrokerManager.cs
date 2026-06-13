namespace LovgaBroker.Services;

using System.Collections.Concurrent;
using Interfaces;

public class BrokerManager : IBrokerManager
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ConcurrentDictionary<string, IMessageBroker> _brokers = new();

    // Tracks which topics have already had their dispatcher Task started.
    // GetOrAdd factory on _brokers can run more than once under concurrency;
    // _startedTopics.TryAdd is the single gate for firing OnBrokerAdded.
    private readonly ConcurrentDictionary<string, byte> _startedTopics = new();

    public BrokerManager(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public event Action<IMessageBroker>? OnBrokerAdded;

    public IMessageBroker GetBroker(string topic)
    {
        // Factory may execute more than once under high concurrency — keep it side-effect-free.
        var broker = _brokers.GetOrAdd(topic, t =>
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var b = scope.ServiceProvider.GetRequiredService<IMessageBroker>();
            b.SetTopic(t);
            return b;
        });

        // Fire exactly once: the thread that wins TryAdd starts the dispatcher.
        if (_startedTopics.TryAdd(topic, 0))
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