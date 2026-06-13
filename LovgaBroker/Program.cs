namespace LovgaBroker;

using GrpcServices;
using GrpcServices.Interceptors;
using GrpcServices.Interfaces;
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

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

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
        builder.Services.AddSingleton<IReceiver, ReceiverService>();

        builder.Services.AddSingleton<SubscriberGrpcServer>();
        builder.Services.AddSingleton<PublisherGrpcServer>();
        builder.Services.AddSingleton<DeadLetterTopicInterceptor>();

        builder.Services.AddSingleton<DataContext>();
        builder.Services.AddTransient<StorageService>();
        builder.Services.AddTransient<IMessageBroker, MessageBroker>();

        var host = builder.Build();

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        context.Init();

        host.Run();
    }
}