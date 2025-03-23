namespace LovgaBroker.Services;

using Models;

public interface IMessageBroker
{
    ValueTask Publish(Message message);
    void Subscribe(string topic, Func<Message, Task> handler);
}