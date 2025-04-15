namespace LovgaBroker.GrpcServices.Interfaces;

using Models;

public interface IConsumerGrpcClient
{
    string Id { get; }

    event Action<string, string> OnRegisterConsumer;
    event Action<string, string> OnUnregisterConsumer;
    void SetUpConsumer(string id, string topic, string host, int port);
    Task<bool> DeliverMessage(Message message);
}