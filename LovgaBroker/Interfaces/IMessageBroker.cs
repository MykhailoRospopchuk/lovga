namespace LovgaBroker.Interfaces;

using GrpcServices.Interfaces;
using Models;

public interface IMessageBroker
{
    string Topic { get; }
    void SetTopic(string topic);
    ValueTask EnqueueMessage(Message message);
    bool Subscribe(string subscriberId, IConsumerGrpcClient consumerGrpcClient);
    bool Unsubscribe(string subscriberId);
    bool ConsumerExists(string subscriberId);
    Task DispatchAsync(CancellationToken cancellationToken);
}