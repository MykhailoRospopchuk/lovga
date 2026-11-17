namespace LovgaBroker.Interfaces;

using Models;

public interface IReceiver
{
    ValueTask<bool> Publish(Message message, CancellationToken cancellationToken = default);
    Task DispatchAsync(CancellationToken cancellationToken);
    Task LoadStoredMessages(CancellationToken cancellationToken = default);
}