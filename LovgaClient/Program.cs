namespace LovgaClient;

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using LovgaCommon;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        Console.Clear();
        Console.WriteLine("=================================================");
        Console.WriteLine("          LOVGA SUBSCRIBER LOAD TESTER           ");
        Console.WriteLine("=================================================");

        string host = "localhost";
        int port = 8080;
        int concurrency = 10;
        int numTopics = 5;
        string baseTopic = "bobr-topic";
        bool isLoadTest = true;

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
                case "--legacy":
                    isLoadTest = false;
                    break;
            }
        }

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
        using var cts = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        var dashboardTask = Task.Run(async () =>
        {
            var lastReceived = 0L;
            var lastTime = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, cts.Token);
                var now = DateTime.UtcNow;
                var elapsed = stopwatch.Elapsed;
                var totalRecv = stats.TotalReceived;
                var totalAcks = stats.TotalAcks;
                var deltaRecv = totalRecv - lastReceived;
                var deltaTime = (now - lastTime).TotalSeconds;
                var throughput = deltaTime > 0 ? deltaRecv / deltaTime : 0;

                double avgLat;
                double minLat;
                double maxLat;
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

        using var sharedChannel = CreateChannel(host, port);

        var tasks = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            var workerId = i;
            tasks[i] = Task.Run(async () =>
            {
                var subscriberId = Guid.NewGuid().ToString();
                var topic = numTopics == 1 ? baseTopic : $"{baseTopic}-{workerId % numTopics}";
                var client = new Subscriber.SubscriberClient(sharedChannel);

                while (!cts.Token.IsCancellationRequested)
                {
                    var ackChannel = System.Threading.Channels.Channel.CreateUnbounded<AcknowledgeAction>(
                        new System.Threading.Channels.UnboundedChannelOptions
                        {
                            SingleReader = true,
                            SingleWriter = true
                        });
                    Task? ackWriterTask = null;

                    try
                    {
                        using var call = client.SubscribeStream(cancellationToken: cts.Token);

                        await call.RequestStream.WriteAsync(new ClientMessage
                        {
                            Subscribe = new SubscribeAction
                            {
                                Topic = topic,
                                ConsumerId = subscriberId
                            }
                        });

                        Interlocked.Increment(ref stats.ActiveConnections);

                        ackWriterTask = Task.Run(async () =>
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
                            catch
                            {
                                // Expected on shutdown or stream reset
                            }
                        });

                        await foreach (var message in call.ResponseStream.ReadAllAsync(cts.Token))
                        {
                            double latencyMs = 0;
                            ReadOnlySpan<char> contentSpan = message.Content.AsSpan();
                            var pipeIndex = contentSpan.IndexOf('|');
                            if (pipeIndex > 0 && long.TryParse(contentSpan[..pipeIndex], out var sentTicks))
                            {
                                var diffTicks = DateTime.UtcNow.Ticks - sentTicks;
                                latencyMs = (double)diffTicks / TimeSpan.TicksPerMillisecond;
                                if (latencyMs < 0) latencyMs = 0;
                            }

                            stats.RecordReceived(latencyMs);

                            ackChannel.Writer.TryWrite(new AcknowledgeAction
                            {
                                MessageId = message.MessageId,
                                Topic = message.Topic
                            });
                        }
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                    {
                        // Expected on shutdown
                    }
                    catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                    {
                        // Expected on shutdown
                    }
                    catch (Exception)
                    {
                        if (!cts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(1000);
                        }
                    }
                    finally
                    {
                        ackChannel.Writer.TryComplete();
                        if (ackWriterTask is not null)
                        {
                            try { await ackWriterTask; } catch { }
                        }
                        Interlocked.Decrement(ref stats.ActiveConnections);
                    }
                }
            });
        }

        var tcsExit = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
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

    private static GrpcChannel CreateChannel(string host, int port)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan
        };

        return GrpcChannel.ForAddress($"http://{host}:{port}", new GrpcChannelOptions
        {
            HttpHandler = handler
        });
    }

    private static async Task RunLegacySubscriber()
    {
        Console.WriteLine("Starting Lovga Client (Bi-directional Streaming)...");
        var subscriberId = Guid.NewGuid().ToString();
        var topic = "bobr-topic";

        using var channel = CreateChannel("localhost", 8080);
        var client = new Subscriber.SubscriberClient(channel);

        using var call = client.SubscribeStream();

        await call.RequestStream.WriteAsync(new ClientMessage
        {
            Subscribe = new SubscribeAction
            {
                Topic = topic,
                ConsumerId = subscriberId
            }
        });

        Console.WriteLine($"Subscribed to topic '{topic}' with ID {subscriberId}. Waiting for messages...");

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in call.ResponseStream.ReadAllAsync())
                {
                    Console.WriteLine($"[Received] MsgId: {message.MessageId} Topic: {message.Topic} Content: {message.Content}");

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

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();

        await call.RequestStream.CompleteAsync();
        await readTask;
    }

    private sealed class Stats
    {
        public int ActiveConnections;
        public long TotalReceived;
        public long TotalAcks;
        public double TotalLatencyMs;
        public double MinLatencyMs = double.MaxValue;
        public double MaxLatencyMs;
        public readonly Lock LockObj = new();

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
