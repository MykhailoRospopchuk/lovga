namespace LovgaBroker.Services;

using Interfaces;
using Models;

public interface IMessageBroker
{
    ValueTask Publish(Message message);
    void Subscribe(string topic, IConsumerObserver consumer);
}