namespace LovgaBroker;

using Services;
using Workers;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddHostedService<Worker>();

        builder.Services.AddSingleton<IMessageBroker, MessageBroker>();
        builder.Services.AddHostedService<BrokerWorker>();

        var host = builder.Build();
        host.Run();
    }
}