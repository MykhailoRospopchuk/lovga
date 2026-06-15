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

    public Task<bool> MarkInFlight(int id) => Task.FromResult(true);

    public Task<bool> ReleaseInFlight(int id) => Task.FromResult(true);

    public async IAsyncEnumerable<Message> GetChunkMessagesAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task DeleteMessage(int id) => Task.CompletedTask;
}
