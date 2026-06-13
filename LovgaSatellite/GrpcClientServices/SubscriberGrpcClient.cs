namespace LovgaSatellite.GrpcClientServices;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using API;
using ActionHolder;
using GrpcChannel;
using LovgaCommon;
using Grpc.Core;
using Models;

public class SubscriberGrpcClient : IAsyncDisposable
{
    private readonly string _subscriberId = Guid.NewGuid().ToString();
    private readonly ConcurrentDictionary<string, byte> _activeTopics = new();
    private readonly GrpcChannelProvider? _channelProvider;
    
    private AsyncDuplexStreamingCall<ClientMessage, ServerMessage>? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private readonly SemaphoreSlim _streamLock = new(1, 1);

    public SubscriberGrpcClient()
    {
        _channelProvider = GrpcChannelProvider.GetInstance();
    }

    private async Task EnsureStreamConnected()
    {
        if (_stream != null && _cts != null && !_cts.IsCancellationRequested)
        {
            return;
        }

        await _streamLock.WaitAsync();
        try
        {
            if (_stream != null && _cts != null && !_cts.IsCancellationRequested)
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
            _readTask = Task.Run(ReadLoop);
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private async Task ReadLoop()
    {
        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            if (_stream == null) return;

            await foreach (var message in _stream.ResponseStream.ReadAllAsync(token))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var action = ActionHolder.GetAction(message.Topic);
                        if (action != null)
                        {
                            action(new ActionModel { Content = message.Content });
                        }

                        await SendClientMessage(new ClientMessage
                        {
                            Ack = new AcknowledgeAction
                            {
                                MessageId = message.MessageId,
                                Topic = message.Topic
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing message {message.MessageId} on topic {message.Topic}: {ex.Message}");
                    }
                }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Subscriber stream disconnected: {ex.Message}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _ = Task.Delay(2000, CancellationToken.None).ContinueWith(async _ =>
                {
                    try
                    {
                        await ReconnectAndResubscribe();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to reconnect subscriber stream: {ex.Message}");
                    }
                });
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

    private async Task SendClientMessage(ClientMessage msg)
    {
        if (_stream == null) return;
        await _stream.RequestStream.WriteAsync(msg);
    }

    public async Task<bool> Subscribe(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            throw new ArgumentNullException(nameof(topic));
        }

        if (_activeTopics.ContainsKey(topic))
        {
            throw new InvalidOperationException("Already subscribed");
        }

        await EnsureStreamConnected();

        await SendClientMessage(new ClientMessage
        {
            Subscribe = new SubscribeAction
            {
                Topic = topic,
                ConsumerId = _subscriberId
            }
        });

        _activeTopics.TryAdd(topic, 0);
        return true;
    }

    public async Task<bool> Unsubscribe(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            throw new ArgumentNullException(nameof(topic));
        }

        if (!_activeTopics.ContainsKey(topic))
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

        _activeTopics.TryRemove(topic, out _);
        return true;
    }

    public string[] GetActiveTopics()
    {
        return _activeTopics.Keys.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_stream != null)
        {
            try
            {
                await _stream.RequestStream.CompleteAsync();
            }
            catch {}
            _stream.Dispose();
        }
        _cts?.Dispose();
    }
}