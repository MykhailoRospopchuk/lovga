namespace TestProject;

using DTO;
using LovgaSatellite.API;
using LovgaSatellite.GrpcClientServices;
using LovgaSatellite.GrpcServerServices;
using LovgaSatellite.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Services;

public class Program
{
    public static void Main(string[] args)
    {
        var messageProcessor = new MessageProcessor();

        var builder = WebApplication.CreateBuilder(args);

        SatelliteExtensions.ConfigureChannel("localhost", 8080);
        SatelliteExtensions.ConfigureHost("localhost", 7880);
        SatelliteExtensions.InitChannel();
        SatelliteExtensions.RegisterAction("bobr-topic", message => messageProcessor.ProcessMessage(message));

        builder.Services.AddHostedService<GrpcServerHostedService>();
        builder.Services.AddScoped<PublisherGrpcClient>();
        builder.Services.AddScoped<SubscriberGrpcClient>();
        builder.Services.AddSingleton<ConsumerGrpcServerService>();

        var app = builder.Build();

        app.MapGet("/", () => "Hello World!");

        app.MapPost("/subscribe", async (SubscriberGrpcClient client) =>
        {
            var result = await client.Subscribe("bobr-topic");

            return result;
        });

        app.MapPost("/publish-message", async (PublisherGrpcClient client, [FromBody] PublishMessageRequestDTO message) =>
        {
            var result = await client.PublishMessage($"{DateTime.UtcNow} - {message.Message}", "bobr-topic");

            return result;
        });

        app.Run();
    }
}
