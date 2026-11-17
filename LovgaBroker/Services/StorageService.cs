namespace LovgaBroker.Services;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dapper;
using Interfaces;
using Models;
using Storage;

public sealed class StorageService : IStorageService, IAsyncDisposable
{
    private readonly DataContext _context;
    private readonly ILogger<StorageService> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private readonly Channel<InsertWorkItem> _insertChannel = Channel.CreateUnbounded<InsertWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Channel<int> _inFlightChannel = Channel.CreateUnbounded<int>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Channel<int> _deleteChannel = Channel.CreateUnbounded<int>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _batchWriterTask;

    private readonly record struct InsertWorkItem(Message Message, TaskCompletionSource<int> Tcs);

    public StorageService(DataContext context, ILogger<StorageService> logger)
    {
        _context = context;
        _logger = logger;
        _batchWriterTask = Task.Run(() => RunWriteBatchLoopAsync(_cts.Token));
    }

    public Task<int> InsertMessage(Message message)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_insertChannel.Writer.TryWrite(new InsertWorkItem(message, tcs)))
        {
            return InsertSingleDirectAsync(message);
        }
        return tcs.Task;
    }

    public ValueTask<bool> MarkInFlight(int id)
    {
        var queued = _inFlightChannel.Writer.TryWrite(id);
        return ValueTask.FromResult(queued);
    }

    public Task DeleteMessage(int id)
    {
        _deleteChannel.Writer.TryWrite(id);
        return Task.CompletedTask;
    }

    private async Task<int> InsertSingleDirectAsync(Message message)
    {
        const string command = """
            INSERT INTO MessageEntity (Topic, Content, CreatedAt)
            VALUES (@Topic, @Content, @CreatedAt)
            RETURNING Id;
            """;

        await _writeGate.WaitAsync();
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QuerySingleAsync<int>(command, message);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to insert message for topic {Topic}", message.Topic);
            return -1;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task RunWriteBatchLoopAsync(CancellationToken cancellationToken)
    {
        var insertBatch = new List<InsertWorkItem>(256);
        var inFlightBatch = new List<int>(256);
        var deleteBatch = new List<int>(256);

        using var connection = _context.CreateConnection();
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA journal_mode=WAL; PRAGMA temp_store=MEMORY;";
            pragmaCmd.ExecuteNonQuery();
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait until at least one queue has work
                if (!_insertChannel.Reader.TryPeek(out _) &&
                    !_inFlightChannel.Reader.TryPeek(out _) &&
                    !_deleteChannel.Reader.TryPeek(out _))
                {
                    var waitInsert = _insertChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    var waitInFlight = _inFlightChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    var waitDelete = _deleteChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    await Task.WhenAny(waitInsert, waitInFlight, waitDelete);
                }

                DrainChannels(insertBatch, inFlightBatch, deleteBatch);

                if (insertBatch.Count == 0 && inFlightBatch.Count == 0 && deleteBatch.Count == 0)
                {
                    continue;
                }

                await FlushBatchesAsync(connection, insertBatch, inFlightBatch, deleteBatch);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SQLite group-commit write loop");
                foreach (var item in insertBatch)
                {
                    item.Tcs.TrySetResult(-1);
                }
                insertBatch.Clear();
                inFlightBatch.Clear();
                deleteBatch.Clear();
            }
        }

        // Final drain on shutdown
        DrainChannels(insertBatch, inFlightBatch, deleteBatch);
        if (insertBatch.Count > 0 || inFlightBatch.Count > 0 || deleteBatch.Count > 0)
        {
            try
            {
                await FlushBatchesAsync(connection, insertBatch, inFlightBatch, deleteBatch);
            }
            catch
            {
                foreach (var item in insertBatch)
                {
                    item.Tcs.TrySetResult(-1);
                }
            }
        }
    }

    private void DrainChannels(
        List<InsertWorkItem> insertBatch,
        List<int> inFlightBatch,
        List<int> deleteBatch)
    {
        while (insertBatch.Count < 512 && _insertChannel.Reader.TryRead(out var ins))
        {
            insertBatch.Add(ins);
        }

        while (inFlightBatch.Count < 1024 && _inFlightChannel.Reader.TryRead(out var inf))
        {
            inFlightBatch.Add(inf);
        }

        while (deleteBatch.Count < 1024 && _deleteChannel.Reader.TryRead(out var del))
        {
            deleteBatch.Add(del);
        }
    }

    private async Task FlushBatchesAsync(
        System.Data.IDbConnection connection,
        List<InsertWorkItem> insertBatch,
        List<int> inFlightBatch,
        List<int> deleteBatch)
    {
        await _writeGate.WaitAsync();
        try
        {
            using var transaction = connection.BeginTransaction();

            if (insertBatch.Count > 0)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO MessageEntity (Topic, Content, CreatedAt)
                    VALUES ($topic, $content, $createdAt)
                    RETURNING Id;
                    """;

                var pTopic = cmd.CreateParameter();
                pTopic.ParameterName = "$topic";
                cmd.Parameters.Add(pTopic);

                var pContent = cmd.CreateParameter();
                pContent.ParameterName = "$content";
                cmd.Parameters.Add(pContent);

                var pCreatedAt = cmd.CreateParameter();
                pCreatedAt.ParameterName = "$createdAt";
                cmd.Parameters.Add(pCreatedAt);

                cmd.Prepare();

                foreach (var item in insertBatch)
                {
                    pTopic.Value = item.Message.Topic;
                    pContent.Value = item.Message.Content;
                    pCreatedAt.Value = item.Message.CreatedAt;

                    var idObj = cmd.ExecuteScalar();
                    var id = Convert.ToInt32(idObj);
                    item.Message.SetId(id);
                }
            }

            if (inFlightBatch.Count > 0)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE MessageEntity SET InFlight = 1, LastDeliveredAt = $now WHERE Id = $id;";

                var pNow = cmd.CreateParameter();
                pNow.ParameterName = "$now";
                pNow.Value = now;
                cmd.Parameters.Add(pNow);

                var pId = cmd.CreateParameter();
                pId.ParameterName = "$id";
                cmd.Parameters.Add(pId);

                cmd.Prepare();

                foreach (var id in inFlightBatch)
                {
                    pId.Value = id;
                    cmd.ExecuteNonQuery();
                }
            }

            if (deleteBatch.Count > 0)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM MessageEntity WHERE Id = $id;";

                var pId = cmd.CreateParameter();
                pId.ParameterName = "$id";
                cmd.Parameters.Add(pId);

                cmd.Prepare();

                foreach (var id in deleteBatch)
                {
                    pId.Value = id;
                    cmd.ExecuteNonQuery();
                }
            }

            transaction.Commit();

            foreach (var item in insertBatch)
            {
                item.Tcs.TrySetResult(item.Message.Id);
            }
        }
        finally
        {
            insertBatch.Clear();
            inFlightBatch.Clear();
            deleteBatch.Clear();
            _writeGate.Release();
        }
    }

    public async Task FlushPendingWritesAsync()
    {
        while (_insertChannel.Reader.TryPeek(out _) ||
               _inFlightChannel.Reader.TryPeek(out _) ||
               _deleteChannel.Reader.TryPeek(out _))
        {
            await Task.Delay(5);
        }

        // Wait for any active batch transaction to commit
        await _writeGate.WaitAsync();
        _writeGate.Release();
    }

    public async Task<bool> ReleaseInFlight(int id)
    {
        const string command = """
            UPDATE MessageEntity
            SET InFlight = 0, LastDeliveredAt = 0
            WHERE Id = @Id;
            """;

        await _writeGate.WaitAsync();
        try
        {
            using var connection = _context.CreateConnection();
            var result = await connection.ExecuteAsync(command, new { Id = id });
            return result > 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error releasing message {Id} from in flight", id);
            return false;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<int> GetMaxMessageIdAsync()
    {
        await FlushPendingWritesAsync();

        const string query = "SELECT COALESCE(MAX(Id), 0) FROM MessageEntity;";
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.ExecuteScalarAsync<int>(query);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error fetching max message Id on startup");
            return 0;
        }
    }

    public async IAsyncEnumerable<Message> GetChunkMessagesAsync(
        int? maxId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await FlushPendingWritesAsync();

        const string query = """
            SELECT Id, Topic, Content, CreatedAt FROM MessageEntity
            WHERE (InFlight = 0 OR (InFlight = 1 AND LastDeliveredAt < @ExpiryThreshold))
              AND Id > @LastId
              AND (@MaxId IS NULL OR Id <= @MaxId)
            ORDER BY Id
            LIMIT @Limit;
            """;

        var hasMore = true;
        var lastId = 0;
        const int limit = 500;
        List<Message> messages;

        using var connection = _context.CreateConnection();

        while (hasMore && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var expiryThreshold = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30;
                var result = await connection.QueryAsync<Message>(query, new
                {
                    LastId = lastId,
                    MaxId = maxId,
                    Limit = limit,
                    ExpiryThreshold = expiryThreshold
                });

                messages = result.AsList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error reading stored messages chunk after Id {LastId}", lastId);
                yield break;
            }

            hasMore = messages.Count == limit;
            if (messages.Count > 0)
            {
                lastId = messages[^1].Id;
            }

            foreach (var message in messages)
            {
                yield return message;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _insertChannel.Writer.TryComplete();
        _inFlightChannel.Writer.TryComplete();
        _deleteChannel.Writer.TryComplete();
        await _cts.CancelAsync();
        try
        {
            await _batchWriterTask;
        }
        catch
        {
        }
        _cts.Dispose();
        _writeGate.Dispose();
    }
}