namespace LovgaClient;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using LovgaCommon;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Clear();
        Console.WriteLine("=================================================");
        Console.WriteLine("          LOVGA SUBSCRIBER LOAD TESTER           ");
        Console.WriteLine("=================================================");

        // Default Configuration values
        string host = "localhost";
        int port = 8080;
        int concurrency = 10;
        int numTopics = 5;
        string baseTopic = "bobr-topic";
        bool isLoadTest = true;
        string group = "default-group";

        // Command-line Argument Parsing
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host":
                    if (i + 1 < args.Length) host = args[++i];
                    break;
                case "--port":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) port = p;
                    break;
                case "--concurrency":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var c)) concurrency = c;
                    break;
                case "--topics":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var t)) numTopics = t;
                    break;
                case "--topic-base":
                    if (i + 1 < args.Length) baseTopic = args[++i];
                    break;
                case "--group":
                    if (i + 1 < args.Length) group = args[++i];
                    break;
                case "--legacy":
                    isLoadTest = false;
                    break;
            }
        }

        // If no CLI args, ask interactively
        if (args.Length == 0)
        {
            Console.WriteLine("Select Mode:");
            Console.WriteLine("1. Run Load Test (Default Settings)");
            Console.WriteLine("2. Run Load Test (Custom Settings)");
            Console.WriteLine("3. Run Legacy Subscriber Mode");
            Console.Write("Choose an option [1]: ");
            var choice = Console.ReadLine()?.Trim() ?? "";

            if (choice == "3")
            {
                isLoadTest = false;
            }
            else if (choice == "2")
            {
                Console.Write($"Enter Host [{host}]: ");
                var val = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(val)) host = val;

                Console.Write($"Enter Port [{port}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valPort)) port = valPort;

                Console.Write($"Enter Concurrency (number of parallel subscribers) [{concurrency}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valCon)) concurrency = valCon;

                Console.Write($"Enter Number of Topics [{numTopics}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valTopics)) numTopics = valTopics;

                Console.Write($"Enter Consumer Group [{group}]: ");
                var valGroup = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(valGroup)) group = valGroup;
            }
        }

        if (!isLoadTest)
        {
            await RunLegacySubscriber();
            return;
        }

        Console.WriteLine($"\n[Config] Host: {host}:{port}");
        Console.WriteLine($"[Config] Concurrency: {concurrency} workers");
        Console.WriteLine($"[Config] Topics: {numTopics} (base: '{baseTopic}')");
        Console.WriteLine($"[Config] Consumer Group: {group}");
        if (args.Length == 0)
        {
            Console.WriteLine("\nPress any key to START subscriber...");
            Console.ReadKey(true);
        }
        else
        {
            Console.WriteLine("\nStarting subscriber automatically (arguments provided)...");
        }

        var stats = new Stats();
        var cts = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        // Start stats dashboard task
        var dashboardTask = Task.Run(async () =>
        {
            var lastReceived = 0L;
            var lastTime = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000);
                var now = DateTime.UtcNow;
                var elapsed = stopwatch.Elapsed;
                var totalRecv = stats.TotalReceived;
                var totalAcks = stats.TotalAcks;
                var deltaRecv = totalRecv - lastReceived;
                var deltaTime = (now - lastTime).TotalSeconds;
                var throughput = deltaTime > 0 ? deltaRecv / deltaTime : 0;

                double avgLat = 0;
                double minLat = 0;
                double maxLat = 0;
                lock (stats.LockObj)
                {
                    avgLat = stats.TotalReceived > 0 ? stats.TotalLatencyMs / stats.TotalReceived : 0;
                    minLat = stats.MinLatencyMs == double.MaxValue ? 0 : stats.MinLatencyMs;
                    maxLat = stats.MaxLatencyMs;
                }

                Console.Clear();
                Console.WriteLine("=================================================");
                Console.WriteLine("         LOVGA SUBSCRIBER LOAD DASHBOARD         ");
                Console.WriteLine("=================================================");
                Console.WriteLine($"Elapsed Time:      {elapsed:hh\\:mm\\:ss}");
                Console.WriteLine($"Active Clients:    {stats.ActiveConnections} / {concurrency}");
                Console.WriteLine($"Messages Received: {totalRecv:N0}");
                Console.WriteLine($"ACKs Sent:         {totalAcks:N0}");
                Console.WriteLine($"Throughput:        {throughput:F2} msg/sec");
                Console.WriteLine($"End-to-End Latency:");
                Console.WriteLine($"  - Average:       {avgLat:F2} ms");
                Console.WriteLine($"  - Min:           {minLat:F2} ms");
                Console.WriteLine($"  - Max:           {maxLat:F2} ms");
                Console.WriteLine("=================================================");
                Console.WriteLine("Press Ctrl+C to stop.");

                lastReceived = totalRecv;
                lastTime = now;
            }
        });

        // Start subscriber tasks
        var tasks = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            var workerId = i;
            tasks[i] = Task.Run(async () =>
            {
                var subscriberId = Guid.NewGuid().ToString();
                var topic = numTopics == 1 ? baseTopic : $"{baseTopic}-{workerId % numTopics}";
                
                while (!cts.Token.IsCancellationRequested)
                {
                    var channel = new Channel(host, port, ChannelCredentials.Insecure);
                    var client = new Subscriber.SubscriberClient(channel);
                    
                    try
                    {
                        using var call = client.SubscribeStream(cancellationToken: cts.Token);
                        
                        // Send Subscribe request
                        await call.RequestStream.WriteAsync(new ClientMessage
                        {
                            Subscribe = new SubscribeAction
                            {
                                Topic = topic,
                                ConsumerId = subscriberId,
                                ConsumerGroup = group
                            }
                        });

                        Interlocked.Increment(ref stats.ActiveConnections);

                        var ackChannel = System.Threading.Channels.Channel.CreateUnbounded<AcknowledgeAction>();

                        // Background ACK writer for this stream
                        var ackWriterTask = Task.Run(async () =>
                        {
                            try
                            {
                                await foreach (var ackAction in ackChannel.Reader.ReadAllAsync(cts.Token))
                                {
                                    await call.RequestStream.WriteAsync(new ClientMessage
                                    {
                                        Ack = ackAction
                                    });
                                    stats.RecordAck();
                                }
                            }
                            catch { }
                        });

                        await foreach (var message in call.ResponseStream.ReadAllAsync(cts.Token))
                        {
                            // Try parsing message content to calculate latency
                            // Format: ticks|workerId|msgSeq|padding
                            double latencyMs = 0;
                            var parts = message.Content.Split('|');
                            if (parts.Length >= 4 && long.TryParse(parts[0], out var sentTicks))
                            {
                                var diffTicks = DateTime.UtcNow.Ticks - sentTicks;
                                latencyMs = (double)diffTicks / TimeSpan.TicksPerMillisecond;
                                // Handle negative/skew cases gracefully
                                if (latencyMs < 0) latencyMs = 0;
                            }

                            stats.RecordReceived(latencyMs);

                            // Queue ACK write asynchronously (non-blocking)
                            ackChannel.Writer.TryWrite(new AcknowledgeAction
                            {
                                MessageId = message.MessageId,
                                Topic = message.Topic
                            });
                        }

                        ackChannel.Writer.Complete();
                        await ackWriterTask;
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                    {
                        // Expected on shutdown
                    }
                    catch (Exception)
                    {
                        // Network error, pause before reconnecting
                        await Task.Delay(1000);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref stats.ActiveConnections);
                        await channel.ShutdownAsync();
                    }
                }
            });
        }

        // Wait for cancellation (e.g. Ctrl+C)
        var tcsExit = new TaskCompletionSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            tcsExit.TrySetResult();
        };

        await tcsExit.Task;
        cts.Cancel();
        
        try
        {
            await Task.WhenAll(tasks);
        }
        catch { }

        try
        {
            await dashboardTask;
        }
        catch { }

        stopwatch.Stop();
        var totalElapsed = stopwatch.Elapsed;
        double finalAvg = stats.TotalReceived > 0 ? stats.TotalLatencyMs / stats.TotalReceived : 0;

        Console.Clear();
        Console.WriteLine("=================================================");
        Console.WriteLine("         LOAD TEST COMPLETE - SUMMARY            ");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Total Duration:      {totalElapsed:hh\\:mm\\:ss\\.fff}");
        Console.WriteLine($"Total Messages:      {stats.TotalReceived:N0}");
        Console.WriteLine($"Total ACKs Sent:     {stats.TotalAcks:N0}");
        Console.WriteLine($"Overall Throughput:  {(stats.TotalReceived / totalElapsed.TotalSeconds):F2} msg/sec");
        Console.WriteLine($"End-to-End Latency:");
        Console.WriteLine($"  - Average:         {finalAvg:F2} ms");
        Console.WriteLine($"  - Min:             {(stats.MinLatencyMs == double.MaxValue ? 0 : stats.MinLatencyMs):F2} ms");
        Console.WriteLine($"  - Max:             {stats.MaxLatencyMs:F2} ms");
        Console.WriteLine("=================================================");
    }

    private static async Task RunLegacySubscriber()
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
                ConsumerId = subscriberId,
                ConsumerGroup = "legacy-group"
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

    private class Stats
    {
        public int ActiveConnections = 0;
        public long TotalReceived = 0;
        public long TotalAcks = 0;
        public double TotalLatencyMs = 0;
        public double MinLatencyMs = double.MaxValue;
        public double MaxLatencyMs = 0;
        public readonly object LockObj = new();

        public void RecordReceived(double latencyMs)
        {
            lock (LockObj)
            {
                TotalReceived++;
                TotalLatencyMs += latencyMs;
                if (latencyMs < MinLatencyMs) MinLatencyMs = latencyMs;
                if (latencyMs > MaxLatencyMs) MaxLatencyMs = latencyMs;
            }
        }

        public void RecordAck()
        {
            Interlocked.Increment(ref TotalAcks);
        }
    }
}
