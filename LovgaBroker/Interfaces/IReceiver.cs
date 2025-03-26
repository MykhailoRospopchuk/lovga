namespace LovgaBroker.Interfaces;

using Models;

public interface IReceiver
{
    ValueTask Publish(Message message);
}