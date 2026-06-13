namespace LovgaBroker.GrpcServices;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Interfaces;
using LovgaCommon;
using Models;
using Services;

public class StreamingConsumer : IConsumerGrpcClient
{
    public string Id { get; }
    private readonly IServerStreamWriter<ServerMessage> _responseStream;
    private readonly StorageService _storageService;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _inFlight = new();

    public StreamingConsumer(
        string id,
        IServerStreamWriter<ServerMessage> responseStream,
        StorageService storageService,
        ILogger logger)
    {
        Id = id;
        _responseStream = responseStream;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<bool> DeliverMessage(Message message)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inFlight.TryAdd(message.Id, tcs))
        {
            return false;
        }

        // Mark as in-flight in SQLite
        await _storageService.MarkInFlight(message.Id);

        try
        {
            await _writeLock.WaitAsync();
            try
            {
                await _responseStream.WriteAsync(new ServerMessage
                {
                    MessageId = message.Id,
                    Topic = message.Topic,
                    Content = message.Content
                });
            }
            finally
            {
                _writeLock.Release();
            }

            // Wait for client ACK or timeout (e.g. 15 seconds)
            using var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using (delayCts.Token.Register(() => tcs.TrySetResult(false)))
            {
                var success = await tcs.Task;
                if (success)
                {
                    await _storageService.DeleteMessage(message.Id);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver message {Id} to subscriber {SubscriberId}", message.Id, Id);
        }
        finally
        {
            _inFlight.TryRemove(message.Id, out _);
        }

        // Release the message in-flight status so it can be re-delivered
        await _storageService.ReleaseInFlight(message.Id);
        return false;
    }

    public void Acknowledge(int messageId)
    {
        if (_inFlight.TryGetValue(messageId, out var tcs))
        {
            tcs.TrySetResult(true);
        }
    }

    public void CancelAll()
    {
        foreach (var kvp in _inFlight)
        {
            kvp.Value.TrySetResult(false);
        }
        _inFlight.Clear();
    }
}
