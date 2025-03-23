namespace LovgaBroker;

using Grpc.Core;
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
        builder.Services.AddSingleton<ConsumerService>();

        var host = builder.Build();

        var consumer = host.Services.GetRequiredService<ConsumerService>();

        Server grpcServer = new Server
        {
            Services = { Consumer.BindService(consumer) },
            Ports = { new ServerPort("localhost", 8080, ServerCredentials.Insecure) }
        };
        grpcServer.Start();

        host.Run();
    }
}