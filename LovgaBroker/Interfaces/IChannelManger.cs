namespace LovgaBroker.Interfaces;

using Grpc.Core;

public interface IChannelManger
{
    Channel? GetChannel(string target);
    void RegisterConsumer(string target, string consumerId);
    void UnregisterConsumer(string target, string consumerId);
    bool ChannelExists(string target);
}