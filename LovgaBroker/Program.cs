namespace LovgaBroker;

using Grpc.Core;
using LovgaCommon;
using Services;
using Services.BackgroundServices;
using Services.GrpcServices;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddHostedService<Worker>();
        builder.Services.AddHostedService<BrokerWorker>();

        builder.Services.AddSingleton<IMessageBroker, MessageBroker>();

        // builder.Services.AddGrpc();
        builder.Services.AddSingleton<SubscriberService>();

        var host = builder.Build();

        var subscriber = host.Services.GetRequiredService<SubscriberService>();

        Server grpcServer = new Server
        {
            Services = { Subscriber.BindService(subscriber) },
            Ports = { new ServerPort("localhost", 8080, ServerCredentials.Insecure) }
        };
        grpcServer.Start();

        host.Run();
    }
}