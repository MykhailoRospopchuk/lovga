namespace LovgaBroker.Interfaces;

using Grpc.Core;

public interface IChannelManger
{
    Channel? GetChannel(string host, int port);
    void RegisterConsumer(string target, string consumerId);
    void UnregisterConsumer(string target, string consumerId);
    bool ChannelExists(string host, int port);
}