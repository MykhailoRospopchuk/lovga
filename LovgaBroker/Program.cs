namespace LovgaBroker;

using GrpcServices;
using GrpcServices.Interceptors;
using GrpcServices.Interfaces;
using Interfaces;
using Models;
using Services;
using Services.BackgroundServices;
using Services.HostedServices;
using Storage;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        var brokerOptions = builder.Configuration
            .GetSection(BrokerOptions.SectionName)
            .Get<BrokerOptions>() ?? new BrokerOptions();

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

        if (brokerOptions.PersistMessages)
        {
            SQLitePCL.Batteries.Init();
            builder.Services.AddSingleton<DataContext>();
            builder.Services.AddTransient<IStorageService, StorageService>();
        }
        else
        {
            builder.Services.AddSingleton<IStorageService, NullStorageService>();
        }

        builder.Services.AddTransient<IMessageBroker, MessageBroker>();

        var host = builder.Build();

        if (brokerOptions.PersistMessages)
        {
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DataContext>();
            context.Init();
        }

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
        logger.LogInformation("Broker mode: {Mode}",
            brokerOptions.PersistMessages ? "Persistent (messages saved to DB)" : "Direct (no persistence)");

        host.Run();
    }
}