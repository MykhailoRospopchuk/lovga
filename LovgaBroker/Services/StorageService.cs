namespace LovgaBroker.Services;

using System.Threading.Channels;
using Dapper;
using Interfaces;
using Models;
using Storage;

public class StorageService : IStorageService, IDisposable
{
    private readonly DataContext _context;
    private readonly ILogger<StorageService> _logger;
    private readonly Channel<(Message Msg, TaskCompletionSource<int> Tcs)> _insertChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;

    public StorageService(DataContext context, ILogger<StorageService> logger)
    {
        _context = context;
        _logger = logger;
        
        _insertChannel = Channel.CreateBounded<(Message, TaskCompletionSource<int>)>(
            new BoundedChannelOptions(20000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _workerTask = Task.Run(BatchWorkerAsync);
    }

    public Task<int> InsertMessage(Message message)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_insertChannel.Writer.TryWrite((message, tcs)))
        {
            // If bounded channel is full, write asynchronously
            return WriteAsyncFallback(message, tcs);
        }
        return tcs.Task;
    }

    private async Task<int> WriteAsyncFallback(Message message, TaskCompletionSource<int> tcs)
    {
        await _insertChannel.Writer.WriteAsync((message, tcs));
        return await tcs.Task;
    }

    private async Task BatchWorkerAsync()
    {
        var batch = new List<(Message Msg, TaskCompletionSource<int> Tcs)>(1000);
        var reader = _insertChannel.Reader;

        var command = 
@"
INSERT INTO MessageEntity (Topic, Content, CreatedAt)
VALUES (@Topic, @Content, @CreatedAt)
RETURNING Id;
";

        while (await reader.WaitToReadAsync(_cts.Token))
        {
            batch.Clear();

            // Read available items
            while (batch.Count < 1000 && reader.TryRead(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count > 0)
            {
                try
                {
                    using var connection = _context.CreateConnection();
                    using var transaction = connection.BeginTransaction();

                    foreach (var item in batch)
                    {
                        var id = connection.QuerySingle<int>(command, item.Msg, transaction);
                        item.Tcs.TrySetResult(id);
                    }

                    transaction.Commit();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error during batch insert");
                    foreach (var item in batch)
                    {
                        item.Tcs.TrySetException(e);
                    }
                }
            }
        }
    }

    public async Task InsertMessagesAsync(IEnumerable<Message> messages)
    {
        var command = @"
INSERT INTO MessageEntity (Topic, Content, CreatedAt)
VALUES (@Topic, @Content, @CreatedAt)
RETURNING Id;
";
        try
        {
            using var connection = _context.CreateConnection();
            using var transaction = connection.BeginTransaction();

            foreach (var msg in messages)
            {
                var id = await connection.QuerySingleAsync<int>(command, msg, transaction);
                msg.SetId(id);
            }

            transaction.Commit();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error during batch insert");
            throw;
        }
    }

    public async Task SaveOffsetAsync(string consumerGroupId, string topic, int lastMessageId)
    {
        var command = @"
INSERT INTO SubscriberOffsetEntity (ConsumerGroupId, Topic, LastMessageId)
VALUES (@ConsumerGroupId, @Topic, @LastMessageId)
ON CONFLICT(ConsumerGroupId, Topic) DO UPDATE SET LastMessageId = @LastMessageId;
";
        try
        {
            using var connection = _context.CreateConnection();
            await connection.ExecuteAsync(command, new { ConsumerGroupId = consumerGroupId, Topic = topic, LastMessageId = lastMessageId });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error saving offset for group {Id} on {Topic}", consumerGroupId, topic);
        }
    }

    public async Task SaveOffsetsAsync(IEnumerable<(string Group, string Topic, int Offset)> offsets)
    {
        var command = @"
INSERT INTO SubscriberOffsetEntity (ConsumerGroupId, Topic, LastMessageId)
VALUES (@ConsumerGroupId, @Topic, @LastMessageId)
ON CONFLICT(ConsumerGroupId, Topic) DO UPDATE SET LastMessageId = @LastMessageId;
";
        try
        {
            using var connection = _context.CreateConnection();
            using var transaction = connection.BeginTransaction();
            
            foreach (var offset in offsets)
            {
                await connection.ExecuteAsync(command, new { ConsumerGroupId = offset.Group, Topic = offset.Topic, LastMessageId = offset.Offset }, transaction);
            }
            transaction.Commit();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error saving batch offsets");
        }
    }

    public async Task<int> GetSubscriberOffsetAsync(string consumerGroupId, string topic)
    {
        var query = @"SELECT LastMessageId FROM SubscriberOffsetEntity WHERE ConsumerGroupId = @ConsumerGroupId AND Topic = @Topic;";
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QuerySingleOrDefaultAsync<int>(query, new { ConsumerGroupId = consumerGroupId, Topic = topic });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error getting offset for group {Id} on {Topic}", consumerGroupId, topic);
            return 0;
        }
    }

    public async Task DeleteMessagesOlderThanAsync(DateTime threshold)
    {
        var command = @"
DELETE FROM MessageEntity WHERE CreatedAt < @ThresholdMs;
";
        try
        {
            // Assuming CreatedAt is stored as unix timestamp milliseconds (or check Message model)
            // Wait, CreatedAt in Message model is DateTimeOffset.ToUnixTimeMilliseconds() or similar?
            // I should use threshold.ToUniversalTime().ToUnixTimeMilliseconds() if it's stored as long
            // Let's assume it's stored as ticks or unix milliseconds based on current implementation
            using var connection = _context.CreateConnection();
            var thresholdValue = new DateTimeOffset(threshold).ToUnixTimeSeconds(); 
            var deletedCount = await connection.ExecuteAsync(command, new { ThresholdMs = thresholdValue });
            _logger.LogInformation("Deleted {Count} messages older than {Threshold}", deletedCount, threshold);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error deleting old messages");
        }
    }

    public async IAsyncEnumerable<Message> GetMessagesAsync(string topic, int fromId, int limit = 500)
    {
        var query = @"
SELECT Id, Topic, Content, CreatedAt FROM MessageEntity
WHERE Topic = @Topic AND Id > @FromId
ORDER BY Id
LIMIT @Limit;
";
        using var connection = _context.CreateConnection();
        IEnumerable<Message> messages = null;
        try
        {
            messages = await connection.QueryAsync<Message>(query, new { Topic = topic, FromId = fromId, Limit = limit });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error reading messages");
        }

        if (messages != null)
        {
            foreach (var message in messages)
            {
                yield return message;
            }
        }
    }

    public void Dispose()
    {
        _insertChannel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            _workerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
        _cts.Dispose();
    }
}