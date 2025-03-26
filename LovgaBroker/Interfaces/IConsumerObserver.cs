namespace LovgaBroker.Interfaces;

using Models;

public interface IConsumerObserver
{
    bool InitChannel();
    Task DeliverMessage(Message message);
}