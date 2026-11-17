namespace LovgaSatellite.GrpcClientServices;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ActionHolder;
using GrpcChannel;
using Grpc.Core;
using LovgaCommon;
using Models;

public sealed class SubscriberGrpcClient : IAsyncDisposable
{
    private readonly string _subscriberId = Guid.NewGuid().ToString();
    private readonly ConcurrentDictionary<string, byte> _activeTopics = new();
    private readonly GrpcChannelProvider? _channelProvider = GrpcChannelProvider.GetInstance();

    private AsyncDuplexStreamingCall<ClientMessage, ServerMessage>? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private readonly SemaphoreSlim _streamLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile bool _disposed;

    private async Task EnsureStreamConnected(CancellationToken cancellationToken = default)
    {
        if (_stream is not null && _cts is { IsCancellationRequested: false })
        {
            return;
        }

        await _streamLock.WaitAsync(cancellationToken);
        try
        {
            if (_stream is not null && _cts is { IsCancellationRequested: false })
            {
                return;
            }

            if (_channelProvider is null)
            {
                throw new InvalidOperationException("Channel Provider is not initialized");
            }

            _channelProvider.ThrowIfChannelNull();

            var client = new Subscriber.SubscriberClient(_channelProvider.Channel);
            _cts = new CancellationTokenSource();
            _stream = client.SubscribeStream(cancellationToken: _cts.Token);
            _readTask = Task.Run(() => ReadLoop(_stream, _cts));
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private async Task ReadLoop(AsyncDuplexStreamingCall<ClientMessage, ServerMessage> stream, CancellationTokenSource streamCts)
    {
        var token = streamCts.Token;
        try
        {
            await foreach (var message in stream.ResponseStream.ReadAllAsync(token))
            {
                _ = ProcessAndAckMessageAsync(message, token);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && _disposed)
        {
            // Expected on DisposeAsync
        }
        catch (OperationCanceledException) when (_disposed)
        {
            // Expected on DisposeAsync
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Subscriber stream disconnected: {ex.Message}");
        }
        finally
        {
            await InvalidateBrokenStreamAsync(stream);

            if (!_disposed)
            {
                _ = ReconnectLoopAsync();
            }
        }
    }

    private async Task ProcessAndAckMessageAsync(ServerMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var action = ActionHolder.GetAction(message.Topic);
            action?.Invoke(new ActionModel { Content = message.Content });

            await SendClientMessage(new ClientMessage
            {
                Ack = new AcknowledgeAction
                {
                    MessageId = message.MessageId,
                    Topic = message.Topic
                }
            }, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Error processing message {message.MessageId} on topic {message.Topic}: {ex.Message}");
        }
    }

    private async Task InvalidateBrokenStreamAsync(AsyncDuplexStreamingCall<ClientMessage, ServerMessage> brokenStream)
    {
        await _streamLock.WaitAsync();
        try
        {
            if (ReferenceEquals(_stream, brokenStream))
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                brokenStream.Dispose();
                _stream = null;
            }
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private async Task ReconnectLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                await Task.Delay(2000);
                if (_disposed)
                {
                    return;
                }

                await ReconnectAndResubscribe();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to reconnect subscriber stream (retrying in 2s): {ex.Message}");
            }
        }
    }

    private async Task ReconnectAndResubscribe()
    {
        await EnsureStreamConnected();
        foreach (var topic in _activeTopics.Keys)
        {
            await SendClientMessage(new ClientMessage
            {
                Subscribe = new SubscribeAction
                {
                    Topic = topic,
                    ConsumerId = _subscriberId
                }
            });
        }
    }

    private async Task SendClientMessage(ClientMessage msg, CancellationToken cancellationToken = default)
    {
        var currentStream = _stream;
        if (currentStream is null)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_stream is not null)
            {
                await _stream.RequestStream.WriteAsync(msg);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> Subscribe(string topic)
    {
        ArgumentException.ThrowIfNullOrEmpty(topic);

        if (!_activeTopics.TryAdd(topic, 0))
        {
            throw new InvalidOperationException($"Already subscribed to topic '{topic}'");
        }

        try
        {
            await EnsureStreamConnected();

            await SendClientMessage(new ClientMessage
            {
                Subscribe = new SubscribeAction
                {
                    Topic = topic,
                    ConsumerId = _subscriberId
                }
            });

            return true;
        }
        catch
        {
            _activeTopics.TryRemove(topic, out _);
            throw;
        }
    }

    public async Task<bool> Unsubscribe(string topic)
    {
        ArgumentException.ThrowIfNullOrEmpty(topic);

        if (!_activeTopics.TryRemove(topic, out _))
        {
            return false;
        }

        await EnsureStreamConnected();

        await SendClientMessage(new ClientMessage
        {
            Unsubscribe = new UnsubscribeAction
            {
                Topic = topic,
                ConsumerId = _subscriberId
            }
        });

        return true;
    }

    public string[] GetActiveTopics()
    {
        return [.. _activeTopics.Keys];
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        if (_stream is not null)
        {
            try
            {
                await _writeLock.WaitAsync();
                try
                {
                    await _stream.RequestStream.CompleteAsync();
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch
            {
                // Ignore stream completion errors on shutdown
            }

            _stream.Dispose();
            _stream = null;
        }

        if (_readTask is not null)
        {
            try
            {
                await _readTask;
            }
            catch
            {
                // Ignore read task completion errors on shutdown
            }
        }

        _cts?.Dispose();
        _writeLock.Dispose();
        _streamLock.Dispose();
    }
}