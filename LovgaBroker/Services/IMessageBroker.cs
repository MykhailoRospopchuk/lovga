namespace LovgaBroker.Services;

using Models;

public interface IMessageBroker
{
    void Publish(Message message);
    void Subscribe(string topic, Func<Message, Task> handler);
}