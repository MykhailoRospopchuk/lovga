namespace LovgaBroker.Interfaces;

using Models;

public interface IStorageService
{
    Task<int> InsertMessage(Message message);
    ValueTask<bool> MarkInFlight(int id);
    Task<bool> ReleaseInFlight(int id);
    Task<int> GetMaxMessageIdAsync();
    IAsyncEnumerable<Message> GetChunkMessagesAsync(int? maxId = null, CancellationToken cancellationToken = default);
    Task DeleteMessage(int id);
}
