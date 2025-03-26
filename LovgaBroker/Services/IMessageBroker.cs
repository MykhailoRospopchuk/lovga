namespace LovgaBroker.Services;

using Interfaces;
using Models;

public interface IMessageBroker
{
    string Topic { get; }
    ValueTask EnqueueMessage(Message message);
    void Subscribe(string subscriberId, IConsumerObserver consumer);
}