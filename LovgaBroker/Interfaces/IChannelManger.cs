namespace LovgaBroker.Interfaces;

using Grpc.Core;

public interface IChannelManger
{
    Channel? GetChannel(string host, int port);
    bool RemoveChannel(string host, int port);
}