namespace LovgaBroker.GrpcServices.Interfaces;

using Models;

public interface IConsumerGrpcClient
{
    bool InitChannel();
    Task<bool> DeliverMessage(Message message);
}