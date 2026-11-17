namespace LovgaBroker.GrpcServices;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Interfaces;
using LovgaCommon;
using Models;

public sealed class StreamingConsumer(
    string id,
    IServerStreamWriter<ServerMessage> responseStream,
    SemaphoreSlim streamWriteLock,
    ILogger logger) : IConsumerGrpcClient
{
    public string Id { get; } = id;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _inFlight = new();

    public async Task<bool> DeliverMessage(Message message, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inFlight.TryAdd(message.Id, tcs))
        {
            return false;
        }

        try
        {
            await streamWriteLock.WaitAsync(cancellationToken);
            try
            {
                await responseStream.WriteAsync(new ServerMessage
                {
                    MessageId = message.Id,
                    Topic = message.Topic,
                    Content = message.Content
                });
            }
            finally
            {
                streamWriteLock.Release();
            }

            await using var reg = cancellationToken.Register(static state =>
            {
                ((TaskCompletionSource<bool>)state!).TrySetResult(false);
            }, tcs);

            return await tcs.Task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver message {Id} to subscriber {SubscriberId}", message.Id, Id);
            return false;
        }
        finally
        {
            _inFlight.TryRemove(message.Id, out _);
        }
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
