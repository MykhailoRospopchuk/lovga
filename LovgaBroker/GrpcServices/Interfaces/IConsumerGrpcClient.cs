namespace LovgaBroker.GrpcServices.Interfaces;

using Models;

public interface IConsumerGrpcClient
{
    string Id { get; }
    ValueTask<bool> DeliverMessage(Message message);
}