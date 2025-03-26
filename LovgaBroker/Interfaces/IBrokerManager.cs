namespace LovgaBroker.Interfaces;

public interface IBrokerManager
{
    event Action<IMessageBroker> OnBrokerAdded; 
    IMessageBroker GetBroker(string topic);
    IEnumerable<IMessageBroker> GetAllBrokers();
}