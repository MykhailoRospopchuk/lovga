namespace LovgaBroker.Interfaces;

using GrpcServices.Interfaces;
using Models;

public interface IMessageBroker
{
    string Topic { get; }
    ValueTask EnqueueMessage(Message message);
    void Subscribe(string subscriberId, IConsumerGrpcClient consumerGrpcClient);
}