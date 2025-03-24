namespace LovgaClient;

using Grpc.Core;
using LovgaCommon;
using Services.GrpcServices;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        var channel = new Channel("localhost:8080", ChannelCredentials.Insecure);
        var client = new ConsumerServer.ConsumerServerClient(channel);

        var reply = client.SubscribeConsumer(new SubscribeRequest
        {
            Url = "http://localhost:7080",
            Topic = "bobr-topic"
        });

        if (!reply.Success)
        {
            Console.WriteLine("Error");
            return;
        }

        Server grpcServer = new Server
        {
            Services = { ConsumerClient.BindService(new ConsumerService()) },
            Ports = { new ServerPort("localhost", 7080, ServerCredentials.Insecure) }
        };

        grpcServer.Start();

        while (true)
        {
            await Task.Delay(1000);
        }
    }
}
