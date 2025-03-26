namespace LovgaPublisher;

using Grpc.Core;
using LovgaCommon;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        var channel = new Channel("localhost", 8080, ChannelCredentials.Insecure);
        var client = new Publisher.PublisherClient(channel);

        var random = new Random();
        for (var i = 0; i < 100; i++)
        {
            var numbers = random.Next(1, 1000);
            for (var j = 0; j < numbers; j++)
            {
                var reply = client.Publish(new PublishRequest
                {
                    Topic = "bobr-topic",
                    Content = $"This is publisher content: {i} : {j}"
                });
                var reply2 = client.Publish(new PublishRequest
                {
                    Topic = "bobr-topic",
                    Content = $"This is publisher content2: {i} : {j}"
                });

                if (!reply.Success)
                {
                    Console.WriteLine("Error");
                }
                if (!reply2.Success)
                {
                    Console.WriteLine("Error2");
                }
            }

            await Task.Delay(numbers);
        }
    }
}
