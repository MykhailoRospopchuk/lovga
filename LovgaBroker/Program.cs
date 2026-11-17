namespace LovgaBroker;

using System.Net;
using GrpcServices;
using GrpcServices.Interceptors;
using Interfaces;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Models;
using Services;
using Services.BackgroundServices;
using Storage;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        var brokerOptions = builder.Configuration
            .GetSection(BrokerOptions.SectionName)
            .Get<BrokerOptions>() ?? new BrokerOptions();

        var port = builder.Configuration.GetValue("Broker:Port", 8080);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            options.SingleLine = true;
        });

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<DeadLetterTopicInterceptor>();
        });

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
            builder.Services.AddSingleton<IStorageService, StorageService>();
        }
        else
        {
            builder.Services.AddSingleton<IStorageService, NullStorageService>();
        }

        builder.Services.AddTransient<IMessageBroker, MessageBroker>();

        var app = builder.Build();

        if (brokerOptions.PersistMessages)
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DataContext>();
            context.Init();
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
        logger.LogInformation("Broker mode: {Mode}",
            brokerOptions.PersistMessages ? "Persistent (messages saved to DB)" : "Direct (no persistence)");

        app.MapGrpcService<SubscriberGrpcServer>();
        app.MapGrpcService<PublisherGrpcServer>();

        app.Run();
    }
}