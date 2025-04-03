namespace LovgaSatellite.HostedServices;

using Grpc.Core;
using GrpcServerServices;
using LovgaCommon;
using Microsoft.Extensions.Hosting;

public class GrpcServerHostedService : IHostedService
{
    private Server? _grpcServer;
    private readonly ConsumerGrpcServerService _consumerGrpcServer;

    public GrpcServerHostedService(ConsumerGrpcServerService consumerGrpcServer)
    {
        _consumerGrpcServer = consumerGrpcServer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _grpcServer = new Server
        {
            Services =
            {
                Consumer.BindService(_consumerGrpcServer)
            },
            Ports = { new ServerPort("localhost", 7880, ServerCredentials.Insecure) }
        };
        _grpcServer.Start();

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_grpcServer is not null)
        {
            await _grpcServer.ShutdownAsync();
        }
    }
}