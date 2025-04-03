namespace TestProject;

using DTO;
using LovgaSatellite;
using LovgaSatellite.GrpcClientServices;
using Microsoft.AspNetCore.Mvc;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        Extensions.InitChannel("localhost", 8080);

        builder.Services.AddScoped<PublisherGrpcClient>();

        var app = builder.Build();

        app.MapPost("/publish-message", async (PublisherGrpcClient client, [FromBody] PublishMessageRequestDTO message) =>
        {
            var result = await client.PublishMessage($"{DateTime.UtcNow} - {message.Message}", "bobr-topic");

            return result;
        });

        app.Run();
    }
}
