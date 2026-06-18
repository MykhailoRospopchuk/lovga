namespace LovgaBroker.GrpcServices;

using System.Threading.Channels;
using Grpc.Core;
using Interfaces;
using LovgaCommon;
using Models;

public class StreamingConsumer : IConsumerGrpcClient
{
    public string Id { get; }
    public string ConsumerGroupId { get; }
    private readonly string _topic;
    private readonly IServerStreamWriter<ServerMessage> _responseStream;
    private readonly ILogger _logger;

    private readonly System.Threading.Channels.Channel<Message> _streamChannel = System.Threading.Channels.Channel.CreateBounded<Message>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });

    private readonly CancellationTokenSource _cts = new();

    public StreamingConsumer(
        string id,
        string consumerGroupId,
        string topic,
        IServerStreamWriter<ServerMessage> responseStream,
        ILogger logger)
    {
        Id = id;
        ConsumerGroupId = consumerGroupId;
        _topic = topic;
        _responseStream = responseStream;
        _logger = logger;
    }

    public async ValueTask<bool> DeliverMessage(Message message)
    {
        try
        {
            await _streamChannel.Writer.WriteAsync(message, _cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void CancelAll()
    {
        _cts.Cancel();
        _streamChannel.Writer.TryComplete();
    }

    public async Task ProcessLoopAsync()
    {
        try
        {
            await foreach (var msg in _streamChannel.Reader.ReadAllAsync(_cts.Token))
            {
                await _responseStream.WriteAsync(new ServerMessage
                {
                    MessageId = msg.Id,
                    Topic = msg.Topic,
                    Content = msg.Content
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancel
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to gRPC stream for subscriber {Id} (Group {Group})", Id, ConsumerGroupId);
        }
    }
}
