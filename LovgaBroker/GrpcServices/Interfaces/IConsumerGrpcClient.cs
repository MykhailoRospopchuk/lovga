namespace LovgaBroker.GrpcServices.Interfaces;

using Models;

public interface IConsumerGrpcClient
{
    string Id { get; } 
    bool InitChannel();
    Task<bool> DeliverMessage(Message message);
}