namespace LovgaBroker.Interfaces;

using Models;

public interface IStorageService
{
    Task<int> InsertMessage(Message message);
    Task<bool> MarkInFlight(int id);
    Task<bool> ReleaseInFlight(int id);
    IAsyncEnumerable<Message> GetChunkMessagesAsync();
    Task DeleteMessage(int id);
}
