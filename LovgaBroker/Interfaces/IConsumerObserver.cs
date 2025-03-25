namespace LovgaBroker.Interfaces;

using Models;

public interface IConsumerObserver
{
    Task DeliverMessage(Message message);
}