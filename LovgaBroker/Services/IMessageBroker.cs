namespace LovgaBroker.Services;

using GrpcServices;
using Models;

public interface IMessageBroker
{
    ValueTask Publish(Message message);
    void Subscribe(string topic, ConsumerService consumer);
}