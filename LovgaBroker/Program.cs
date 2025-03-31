namespace LovgaBroker;

using Grpc.Core;
using GrpcServices;
using Interfaces;
using LovgaCommon;
using Services;
using Services.BackgroundServices;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddHostedService<BrokerWorker>();
        builder.Services.AddHostedService<ReceiverWorker>();

        builder.Services.AddSingleton<IBrokerManager, BrokerManager>();
        builder.Services.AddSingleton<IReceiver, ReceiverService>();

        // builder.Services.AddGrpc();
        builder.Services.AddSingleton<SubscriberGrpcServer>();
        builder.Services.AddSingleton<PublisherGrpcServer>();

        var host = builder.Build();

        using var scope = host.Services.CreateAsyncScope();
        var subscriber = scope.ServiceProvider.GetRequiredService<SubscriberGrpcServer>();
        var publisher = scope.ServiceProvider.GetRequiredService<PublisherGrpcServer>();

        Server grpcServer = new Server
        {
            Services =
            {
                Subscriber.BindService(subscriber),
                Publisher.BindService(publisher),
            },
            Ports = { new ServerPort("localhost", 8080, ServerCredentials.Insecure) }
        };
        grpcServer.Start();

        host.Run();
    }
}