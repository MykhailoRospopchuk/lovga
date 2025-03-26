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

        builder.Services.AddHostedService<Worker>();
        builder.Services.AddHostedService<BrokerWorker>();
        builder.Services.AddHostedService<ReceiverWorker>();

        builder.Services.AddSingleton<IBrokerManager, BrokerManager>();
        builder.Services.AddSingleton<IReceiver, ReceiverService>();

        // builder.Services.AddGrpc();
        builder.Services.AddSingleton<SubscriberGrpcServer>();

        var host = builder.Build();

        var subscriber = host.Services.GetRequiredService<SubscriberGrpcServer>();
        var publisher = host.Services.GetRequiredService<PublisherGrpcServer>();

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