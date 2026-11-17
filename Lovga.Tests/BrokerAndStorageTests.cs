namespace Lovga.Tests;

using System.Collections.Concurrent;
using Grpc.Core;
using LovgaBroker.GrpcServices;
using LovgaBroker.GrpcServices.Interfaces;
using LovgaBroker.Interfaces;
using LovgaBroker.Models;
using LovgaBroker.Services;
using LovgaBroker.Storage;
using LovgaCommon;
using LovgaCommon.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class BrokerAndStorageTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly DataContext _dataContext;
    private readonly StorageService _storageService;

    public BrokerAndStorageTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"lovga_test_{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Storage"] = $"Data Source={_tempDbPath}"
            })
            .Build();

        _dataContext = new DataContext(config);
        _dataContext.Init();
        _storageService = new StorageService(_dataContext, NullLogger<StorageService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
            if (File.Exists(_tempDbPath + "-wal")) File.Delete(_tempDbPath + "-wal");
            if (File.Exists(_tempDbPath + "-shm")) File.Delete(_tempDbPath + "-shm");
        }
        catch
        {
            // Ignore temp file cleanup errors
        }
    }

    [Fact]
    public async Task StorageService_CrudAndStartupWatermark_OnlyRecoversUpToMaxId()
    {
        var id1 = await _storageService.InsertMessage(new Message { Topic = "t1", Content = "m1" });
        var id2 = await _storageService.InsertMessage(new Message { Topic = "t1", Content = "m2" });
        Assert.True(id1 > 0);
        Assert.True(id2 > id1);

        var maxIdAtStartup = await _storageService.GetMaxMessageIdAsync();
        Assert.Equal(id2, maxIdAtStartup);

        // Simulate a live message inserted AFTER startup watermark was captured
        var liveId3 = await _storageService.InsertMessage(new Message { Topic = "t1", Content = "m3-live" });
        Assert.True(liveId3 > maxIdAtStartup);

        var recovered = new List<Message>();
        await foreach (var msg in _storageService.GetChunkMessagesAsync(maxIdAtStartup))
        {
            recovered.Add(msg);
        }

        Assert.Equal(2, recovered.Count);
        Assert.Equal([id1, id2], recovered.Select(m => m.Id).ToArray());
    }

    [Fact]
    public async Task MessageBroker_MultiSubscriberFanOut_DeletesMessageOnlyAfterAllSubscribersFinish()
    {
        var fakeReceiver = new FakeReceiver();
        var broker = new MessageBroker(NullLogger<MessageBroker>.Instance, fakeReceiver, _storageService);
        broker.SetTopic("orders");

        var msg = new Message { Topic = "orders", Content = "order-123" };
        var id = await _storageService.InsertMessage(msg);
        msg.SetId(id);

        var sub1AckGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sub2AckGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var sub1 = new GateConsumer("sub-1", sub1AckGate.Task);
        var sub2 = new GateConsumer("sub-2", sub2AckGate.Task);

        Assert.True(broker.Subscribe(sub1.Id, sub1));
        Assert.True(broker.Subscribe(sub2.Id, sub2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var dispatchTask = broker.DispatchAsync(cts.Token);

        await broker.EnqueueMessage(msg);

        // Wait until both subscribers have received the message
        await Task.WhenAll(sub1.Received.Task, sub2.Received.Task).WaitAsync(cts.Token);

        // Sub 1 ACKs first — message MUST still exist in SQLite while Sub 2 is processing!
        sub1AckGate.SetResult(true);
        await Task.Delay(50);

        var maxIdWhileSub2Pending = await _storageService.GetMaxMessageIdAsync();
        Assert.Equal(id, maxIdWhileSub2Pending);

        // Now Sub 2 ACKs — message should be deleted from SQLite and not routed to DLQ
        sub2AckGate.SetResult(true);
        await WaitUntilAsync(async () => await _storageService.GetMaxMessageIdAsync() == 0, TimeSpan.FromSeconds(2));

        Assert.Empty(fakeReceiver.PublishedMessages);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatchTask);
    }

    [Fact]
    public async Task MessageBroker_WhenAllSubscribersFail_RoutesToDeadLetterQueueAndEvictsSubscribers()
    {
        var fakeReceiver = new FakeReceiver();
        var broker = new MessageBroker(NullLogger<MessageBroker>.Instance, fakeReceiver, _storageService);
        broker.SetTopic("payments");

        var msg = new Message { Topic = "payments", Content = "pay-999" };
        var id = await _storageService.InsertMessage(msg);
        msg.SetId(id);

        var failingSub1 = new GateConsumer("fail-1", Task.FromResult(false));
        var failingSub2 = new GateConsumer("fail-2", Task.FromResult(false));

        broker.Subscribe(failingSub1.Id, failingSub1);
        broker.Subscribe(failingSub2.Id, failingSub2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var dispatchTask = broker.DispatchAsync(cts.Token);

        await broker.EnqueueMessage(msg);

        await WaitUntilAsync(() => Task.FromResult(fakeReceiver.PublishedMessages.Count == 1), TimeSpan.FromSeconds(2));

        Assert.Single(fakeReceiver.PublishedMessages);
        Assert.Equal(QueueTopic.DeadLetterQueue, fakeReceiver.PublishedMessages.First().Topic);
        Assert.Equal("pay-999", fakeReceiver.PublishedMessages.First().Content);
        Assert.False(broker.ConsumerExists("fail-1"));
        Assert.False(broker.ConsumerExists("fail-2"));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatchTask);
    }

    [Fact]
    public async Task StreamingConsumer_SharedWriteLock_SerializesConcurrentWritesOnSameResponseStream()
    {
        var strictStreamWriter = new SingleWriterCheckingStreamWriter();
        using var sharedWriteLock = new SemaphoreSlim(1, 1);

        var consumerA = new StreamingConsumer("c1", strictStreamWriter, sharedWriteLock, NullLogger.Instance);
        var consumerB = new StreamingConsumer("c2", strictStreamWriter, sharedWriteLock, NullLogger.Instance);

        // Automatically ACK every written message
        strictStreamWriter.OnWrite = serverMsg =>
        {
            if (serverMsg.Topic == "topic-a") consumerA.Acknowledge(serverMsg.MessageId);
            if (serverMsg.Topic == "topic-b") consumerB.Acknowledge(serverMsg.MessageId);
        };

        var tasks = new List<Task<bool>>();
        for (int i = 1; i <= 40; i++)
        {
            var msgA = new Message { Topic = "topic-a", Content = $"a-{i}" };
            msgA.SetId(i * 2);
            var msgB = new Message { Topic = "topic-b", Content = $"b-{i}" };
            msgB.SetId(i * 2 + 1);

            tasks.Add(consumerA.DeliverMessage(msgA));
            tasks.Add(consumerB.DeliverMessage(msgB));
        }

        var results = await Task.WhenAll(tasks);
        Assert.All(results, Assert.True);
        Assert.Equal(80, strictStreamWriter.TotalWrites);
        Assert.False(strictStreamWriter.ConcurrentWriteDetected);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await condition()) return;
            await Task.Delay(20);
        }
        Assert.True(await condition(), "Condition was not met within timeout.");
    }

    private sealed class FakeReceiver : IReceiver
    {
        public ConcurrentQueue<Message> PublishedMessages { get; } = new();

        public ValueTask<bool> Publish(Message message, CancellationToken cancellationToken = default)
        {
            PublishedMessages.Enqueue(message);
            return ValueTask.FromResult(true);
        }

        public Task DispatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadStoredMessages(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class GateConsumer(string id, Task<bool> outcomeTask) : IConsumerGrpcClient
    {
        public string Id { get; } = id;
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> DeliverMessage(Message message, CancellationToken cancellationToken = default)
        {
            Received.TrySetResult();
            return await outcomeTask.WaitAsync(cancellationToken);
        }
    }

    private sealed class SingleWriterCheckingStreamWriter : IServerStreamWriter<ServerMessage>
    {
        private int _activeWriters;
        public int TotalWrites;
        public bool ConcurrentWriteDetected;
        public Action<ServerMessage>? OnWrite;
        public WriteOptions? WriteOptions { get; set; }

        public async Task WriteAsync(ServerMessage message)
        {
            if (Interlocked.Increment(ref _activeWriters) > 1)
            {
                ConcurrentWriteDetected = true;
                throw new InvalidOperationException("Only one write can be pending at a time");
            }

            try
            {
                await Task.Delay(2);
                Interlocked.Increment(ref TotalWrites);
                OnWrite?.Invoke(message);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWriters);
            }
        }
    }
}
