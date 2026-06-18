namespace LovgaBroker.Services;

using Interfaces;
using Models;

/// <summary>
/// No-op storage implementation used in Direct mode.
/// Messages are never persisted — they flow straight through to consumers.
/// </summary>
public class NullStorageService : IStorageService
{
    private static int _idCounter;

    public Task<int> InsertMessage(Message message)
        => Task.FromResult(Interlocked.Increment(ref _idCounter));

    public Task InsertMessagesAsync(IEnumerable<Message> messages)
    {
        foreach (var msg in messages)
        {
            msg.SetId(Interlocked.Increment(ref _idCounter));
        }
        return Task.CompletedTask;
    }

    public Task SaveOffsetAsync(string consumerGroupId, string topic, int lastMessageId) => Task.CompletedTask;
    
    public Task SaveOffsetsAsync(IEnumerable<(string Group, string Topic, int Offset)> offsets) => Task.CompletedTask;

    public Task<int> GetSubscriberOffsetAsync(string consumerGroupId, string topic) => Task.FromResult(0);

    public async IAsyncEnumerable<Message> GetMessagesAsync(string topic, int fromId, int limit = 500)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task DeleteMessagesOlderThanAsync(DateTime threshold) => Task.CompletedTask;
}
