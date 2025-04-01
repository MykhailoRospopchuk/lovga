namespace LovgaBroker;

using GrpcServices;
using GrpcServices.Interceptors;
using Interfaces;
using Services;
using Services.BackgroundServices;
using Services.HostedServices;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddHostedService<GrpcServerHostedService>();
        builder.Services.AddHostedService<BrokerWorker>();
        builder.Services.AddHostedService<ReceiverWorker>();

        builder.Services.AddSingleton<IBrokerManager, BrokerManager>();
        builder.Services.AddSingleton<IReceiver, ReceiverService>();

        builder.Services.AddSingleton<SubscriberGrpcServer>();
        builder.Services.AddSingleton<PublisherGrpcServer>();
        builder.Services.AddSingleton<DeadLetterTopicInterceptor>();

        var host = builder.Build();

        host.Run();
    }
}