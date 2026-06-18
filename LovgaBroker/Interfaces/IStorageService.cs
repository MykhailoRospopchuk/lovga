namespace LovgaBroker.Interfaces;

using Models;

public interface IStorageService
{
    Task<int> InsertMessage(Message message);
    Task InsertMessagesAsync(IEnumerable<Message> messages);
    Task SaveOffsetAsync(string consumerGroupId, string topic, int lastMessageId);
    Task SaveOffsetsAsync(IEnumerable<(string Group, string Topic, int Offset)> offsets);
    Task<int> GetSubscriberOffsetAsync(string consumerGroupId, string topic);
    IAsyncEnumerable<Message> GetMessagesAsync(string topic, int fromId, int limit = 500);
    Task DeleteMessagesOlderThanAsync(DateTime threshold);
}
