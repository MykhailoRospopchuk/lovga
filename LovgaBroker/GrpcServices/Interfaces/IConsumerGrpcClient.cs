namespace LovgaBroker.GrpcServices.Interfaces;

using Models;

public interface IConsumerGrpcClient
{
    string Id { get; } 
    Task<bool> DeliverMessage(Message message);
}