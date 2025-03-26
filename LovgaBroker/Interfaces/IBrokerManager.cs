namespace LovgaBroker.Interfaces;

using Services;

public interface IBrokerManager
{
    IMessageBroker GetBroker(string topic);
    IEnumerable<IMessageBroker> GetAllBrokers();
}