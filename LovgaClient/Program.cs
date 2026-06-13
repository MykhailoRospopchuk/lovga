namespace LovgaClient;

using System;
using System.Threading.Tasks;
using Grpc.Core;
using LovgaCommon;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Lovga Client (Bi-directional Streaming)...");
        var subscriberId = Guid.NewGuid().ToString();
        var topic = "bobr-topic";

        var channel = new Channel("localhost", 8080, ChannelCredentials.Insecure);
        var client = new Subscriber.SubscriberClient(channel);

        using var call = client.SubscribeStream();

        // 1. Send Subscribe request
        await call.RequestStream.WriteAsync(new ClientMessage
        {
            Subscribe = new SubscribeAction
            {
                Topic = topic,
                ConsumerId = subscriberId
            }
        });

        Console.WriteLine($"Subscribed to topic '{topic}' with ID {subscriberId}. Waiting for messages...");

        // 2. Read messages from broker in a loop
        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in call.ResponseStream.ReadAllAsync())
                {
                    Console.WriteLine($"[Received] MsgId: {message.MessageId} Topic: {message.Topic} Content: {message.Content}");

                    // Send ACK back to the broker
                    await call.RequestStream.WriteAsync(new ClientMessage
                    {
                        Ack = new AcknowledgeAction
                        {
                            MessageId = message.MessageId,
                            Topic = message.Topic
                        }
                    });
                    Console.WriteLine($"[ACK] MsgId: {message.MessageId} acknowledged");
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Console.WriteLine("Stream subscription cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading stream: {ex.Message}");
            }
        });

        // Keep running until user terminates
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();

        // Clean up
        await call.RequestStream.CompleteAsync();
        await readTask;
    }
}
