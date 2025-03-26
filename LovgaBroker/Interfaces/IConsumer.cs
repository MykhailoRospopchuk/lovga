namespace LovgaBroker.Interfaces;

using Models;

public interface IConsumer
{
    bool InitChannel();
    Task DeliverMessage(Message message);
}