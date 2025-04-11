namespace LovgaBroker.GrpcServices.Interfaces;

using Grpc.Core;
using Models;

public interface IConsumerGrpcClient
{
    string Id { get; }

    event Action<string, string> OnRegisterConsumer;
    event Action<string, string> OnUnregisterConsumer;
    void SetUpConsumer(Channel channel, string id, string topic);
    Task<bool> DeliverMessage(Message message);
}