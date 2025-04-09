namespace LovgaBroker;

using GrpcServices;
using GrpcServices.Interceptors;
using Interfaces;
using Services;
using Services.BackgroundServices;
using Services.HostedServices;
using Storage;

public class Program
{
    public static void Main(string[] args)
    {
        SQLitePCL.Batteries.Init();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddLogging(configure => 
            configure.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                options.SingleLine = true;
            }));

        builder.Services.AddHostedService<GrpcServerHostedService>();
        builder.Services.AddHostedService<BrokerWorker>();
        builder.Services.AddHostedService<ReceiverWorker>();

        builder.Services.AddSingleton<IBrokerManager, BrokerManager>();
        builder.Services.AddSingleton<IChannelManger, ChannelManger>();
        builder.Services.AddSingleton<IReceiver, ReceiverService>();

        builder.Services.AddSingleton<SubscriberGrpcServer>();
        builder.Services.AddSingleton<PublisherGrpcServer>();
        builder.Services.AddSingleton<DeadLetterTopicInterceptor>();

        builder.Services.AddSingleton<DataContext>();
        builder.Services.AddTransient<StorageService>();

        var host = builder.Build();

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        context.Init();

        var receiver = host.Services.GetRequiredService<IReceiver>();
        receiver.LoadStoredMessages().GetAwaiter().GetResult();

        host.Run();
    }
}