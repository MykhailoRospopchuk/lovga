namespace LovgaBroker.Services;

using System.Runtime.CompilerServices;
using Interfaces;
using Models;

/// <summary>
/// No-op storage implementation used in Direct mode.
/// Messages are never persisted — they flow straight through to consumers.
/// </summary>
public sealed class NullStorageService : IStorageService
{
    private static int _idCounter;

    public Task<int> InsertMessage(Message message)
        => Task.FromResult(Interlocked.Increment(ref _idCounter));

    public ValueTask<bool> MarkInFlight(int id) => ValueTask.FromResult(true);

    public Task<bool> ReleaseInFlight(int id) => Task.FromResult(true);

    public Task<int> GetMaxMessageIdAsync() => Task.FromResult(0);

    public async IAsyncEnumerable<Message> GetChunkMessagesAsync(
        int? maxId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task DeleteMessage(int id) => Task.CompletedTask;
}
