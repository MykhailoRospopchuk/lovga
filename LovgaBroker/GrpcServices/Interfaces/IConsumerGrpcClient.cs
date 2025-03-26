namespace LovgaBroker.GrpcServices.Interfaces;

using Models;

public interface IConsumerGrpcClient
{
    bool InitChannel();
    Task DeliverMessage(Message message);
}