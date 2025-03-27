namespace LovgaClient;

using Grpc.Core;
using LovgaCommon;
using Services.GrpcServices;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        var subscriberId = Guid.NewGuid().ToString();
        var topic = "bobr-topic";

        var channel = new Channel("localhost", 8080, ChannelCredentials.Insecure);
        var client = new Subscriber.SubscriberClient(channel);

        var subscribeReply = client.Subscribe(new SubscribeRequest
        {
            Host = "localhost",
            Port = 7080,
            Topic = topic,
            Id = subscriberId
        });

        if (!subscribeReply.Success)
        {
            Console.WriteLine("Error");
            return;
        }

        Server grpcServer = new Server
        {
            Services = { Consumer.BindService(new ConsumerService()) },
            Ports = { new ServerPort("localhost", 7080, ServerCredentials.Insecure) }
        };

        grpcServer.Start();

        while (true)
        {
            await Task.Delay(1000);
        }

        var unsubscribeReply = client.UnSubscribe(new UnsubscribeRequest
        {
            Topic = topic,
            Id = subscriberId
        });

        if (!unsubscribeReply.Success)
        {
            Console.WriteLine("Error");
            return;
        }
    }
}
