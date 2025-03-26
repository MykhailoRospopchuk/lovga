namespace LovgaBroker.Interfaces;

public interface IBrokerManager
{
    IMessageBroker GetBroker(string topic);
    IEnumerable<IMessageBroker> GetAllBrokers();
}