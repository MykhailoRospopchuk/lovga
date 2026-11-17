namespace LovgaPublisher;

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using LovgaCommon;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        Console.Clear();
        Console.WriteLine("=================================================");
        Console.WriteLine("          LOVGA PUBLISHER LOAD TESTER            ");
        Console.WriteLine("=================================================");

        string host = "localhost";
        int port = 8080;
        int concurrency = 10;
        int numTopics = 5;
        int messagesPerWorker = 1000;
        int delayMs = 0;
        int payloadSize = 100;
        string baseTopic = "bobr-topic";
        bool isLoadTest = true;
        int pipeline = 1;

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
                case "--messages":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var m)) messagesPerWorker = m;
                    break;
                case "--delay":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var d)) delayMs = d;
                    break;
                case "--size":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var s)) payloadSize = s;
                    break;
                case "--topic-base":
                    if (i + 1 < args.Length) baseTopic = args[++i];
                    break;
                case "--pipeline":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var pipe)) pipeline = pipe;
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
            Console.WriteLine("3. Run Legacy Publisher Mode");
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

                Console.Write($"Enter Concurrency (number of parallel publishers) [{concurrency}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valCon)) concurrency = valCon;

                Console.Write($"Enter Number of Topics [{numTopics}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valTopics)) numTopics = valTopics;

                Console.Write($"Enter Messages per Publisher [{messagesPerWorker}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valMsg)) messagesPerWorker = valMsg;

                Console.Write($"Enter Delay between publishes in ms [{delayMs}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valDelay)) delayMs = valDelay;

                Console.Write($"Enter Payload Size in bytes [{payloadSize}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valSize)) payloadSize = valSize;

                Console.Write($"Enter Pipeline Depth (in-flight per worker) [{pipeline}]: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var valPipe)) pipeline = valPipe;
            }
        }

        if (!isLoadTest)
        {
            await RunLegacyPublisher();
            return;
        }

        Console.WriteLine($"\n[Config] Host: {host}:{port}");
        Console.WriteLine($"[Config] Concurrency: {concurrency} workers");
        Console.WriteLine($"[Config] Topics: {numTopics} (base: '{baseTopic}')");
        Console.WriteLine($"[Config] Messages/worker: {messagesPerWorker} (Total: {concurrency * messagesPerWorker})");
        Console.WriteLine($"[Config] Delay: {delayMs} ms");
        Console.WriteLine($"[Config] Payload size: {payloadSize} bytes");
        Console.WriteLine($"[Config] Pipeline depth: {pipeline}");
        if (args.Length == 0)
        {
            Console.WriteLine("\nPress any key to START load test...");
            Console.ReadKey(true);
        }
        else
        {
            Console.WriteLine("\nStarting load test automatically (arguments provided)...");
        }

        var stats = new Stats();
        using var cts = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        var dashboardTask = Task.Run(async () =>
        {
            var lastSent = 0L;
            var lastTime = DateTime.UtcNow;
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, cts.Token);
                var now = DateTime.UtcNow;
                var elapsed = stopwatch.Elapsed;
                var totalSent = stats.TotalSent;
                var totalFailed = stats.TotalFailed;
                var deltaSent = totalSent - lastSent;
                var deltaTime = (now - lastTime).TotalSeconds;
                var throughput = deltaTime > 0 ? deltaSent / deltaTime : 0;

                double avgLat;
                double minLat;
                double maxLat;
                lock (stats.LockObj)
                {
                    avgLat = stats.TotalSent > 0 ? stats.TotalLatencyMs / stats.TotalSent : 0;
                    minLat = stats.MinLatencyMs == double.MaxValue ? 0 : stats.MinLatencyMs;
                    maxLat = stats.MaxLatencyMs;
                }

                Console.Clear();
                Console.WriteLine("=================================================");
                Console.WriteLine("          LOVGA PUBLISHER LOAD DASHBOARD        ");
                Console.WriteLine("=================================================");
                Console.WriteLine($"Elapsed Time:      {elapsed:hh\\:mm\\:ss}");
                Console.WriteLine($"Active Workers:    {stats.ActiveWorkers} / {concurrency}");
                Console.WriteLine($"Messages Sent:     {totalSent:N0} (Failed: {totalFailed:N0})");
                Console.WriteLine($"Throughput:        {throughput:F2} msg/sec");
                Console.WriteLine($"Publish Latency:");
                Console.WriteLine($"  - Average:       {avgLat:F2} ms");
                Console.WriteLine($"  - Min:           {minLat:F2} ms");
                Console.WriteLine($"  - Max:           {maxLat:F2} ms");
                Console.WriteLine("=================================================");
                Console.WriteLine("Press Ctrl+C to terminate early.");

                lastSent = totalSent;
                lastTime = now;
            }
        });

        using var sharedChannel = CreateChannel(host, port);
        var tasks = new Task[concurrency];
        var padding = new string('x', payloadSize);

        for (int i = 0; i < concurrency; i++)
        {
            var workerId = i;
            tasks[i] = Task.Run(async () =>
            {
                Interlocked.Increment(ref stats.ActiveWorkers);
                var client = new Publisher.PublisherClient(sharedChannel);
                var topic = numTopics == 1 ? baseTopic : $"{baseTopic}-{workerId % numTopics}";

                try
                {
                    var activeTasks = new List<Task>(pipeline * 2);
                    using var semaphore = new SemaphoreSlim(pipeline);

                    for (int msgSeq = 0; msgSeq < messagesPerWorker && !cts.Token.IsCancellationRequested; msgSeq++)
                    {
                        var ticks = DateTime.UtcNow.Ticks;
                        var content = $"{ticks}|W{workerId}|S{msgSeq}|{padding}";

                        await semaphore.WaitAsync(cts.Token);

                        var startTime = Stopwatch.GetTimestamp();
                        var publishTask = client.PublishAsync(
                            new PublishRequest
                            {
                                Topic = topic,
                                Content = content
                            },
                            cancellationToken: cts.Token).ResponseAsync;

                        var continuation = publishTask.ContinueWith(
                            t =>
                            {
                                semaphore.Release();
                                var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                                if (t.Status == TaskStatus.RanToCompletion && t.Result.Success)
                                {
                                    stats.RecordSuccess(elapsedMs);
                                }
                                else
                                {
                                    stats.RecordFailure();
                                }
                            },
                            TaskContinuationOptions.ExecuteSynchronously);

                        activeTasks.Add(continuation);

                        if (activeTasks.Count > pipeline * 2)
                        {
                            activeTasks.RemoveAll(static t => t.IsCompleted);
                        }

                        if (delayMs > 0)
                        {
                            await Task.Delay(delayMs, cts.Token);
                        }
                    }

                    await Task.WhenAll(activeTasks);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Worker {workerId} encountered error: {ex.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref stats.ActiveWorkers);
                }
            });
        }

        await Task.WhenAll(tasks);
        cts.Cancel();
        try
        {
            await dashboardTask;
        }
        catch (OperationCanceledException) { }

        stopwatch.Stop();
        var totalElapsed = stopwatch.Elapsed;
        double finalAvg = stats.TotalSent > 0 ? stats.TotalLatencyMs / stats.TotalSent : 0;

        Console.Clear();
        Console.WriteLine("=================================================");
        Console.WriteLine("          LOAD TEST COMPLETE - SUMMARY           ");
        Console.WriteLine("=================================================");
        Console.WriteLine($"Total Duration:      {totalElapsed:hh\\:mm\\:ss\\.fff}");
        Console.WriteLine($"Total Messages:      {concurrency * messagesPerWorker}");
        Console.WriteLine($"Successfully Sent:   {stats.TotalSent:N0}");
        Console.WriteLine($"Failed Messages:     {stats.TotalFailed:N0}");
        Console.WriteLine($"Overall Throughput:  {(stats.TotalSent / totalElapsed.TotalSeconds):F2} msg/sec");
        Console.WriteLine($"Publish Latency:");
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

    private static async Task RunLegacyPublisher()
    {
        Console.WriteLine("Running legacy publisher...");
        using var channel = CreateChannel("localhost", 8080);
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
                var reply3 = client.Publish(new PublishRequest
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
                if (!reply3.Success)
                {
                    Console.WriteLine("Error3");
                }
            }
            await Task.Delay(numbers * 100);
        }
    }

    private sealed class Stats
    {
        public int ActiveWorkers;
        public long TotalSent;
        public long TotalFailed;
        public double TotalLatencyMs;
        public double MinLatencyMs = double.MaxValue;
        public double MaxLatencyMs;
        public readonly Lock LockObj = new();

        public void RecordSuccess(double latencyMs)
        {
            lock (LockObj)
            {
                TotalSent++;
                TotalLatencyMs += latencyMs;
                if (latencyMs < MinLatencyMs) MinLatencyMs = latencyMs;
                if (latencyMs > MaxLatencyMs) MaxLatencyMs = latencyMs;
            }
        }

        public void RecordFailure()
        {
            Interlocked.Increment(ref TotalFailed);
        }
    }
}
