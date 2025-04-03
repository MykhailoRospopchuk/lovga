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
        var builder = WebApplication.CreateBuilder(args);
        Extensions.InitChannel("localhost", 8080);
        Extensions.RegisterAction("bobr-topic", message => MessageProcessor.ProcessMessage(message));

        builder.Services.AddHostedService<GrpcServerHostedService>();
        builder.Services.AddScoped<PublisherGrpcClient>();
        builder.Services.AddScoped<SubscriberGrpcClient>();
        builder.Services.AddSingleton<ConsumerGrpcServerService>();

        var app = builder.Build();

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
