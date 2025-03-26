namespace LovgaBroker.Services;

using Interfaces;
using Models;

public class ReceiverService : IReceiver
{
    public ValueTask Publish(Message message)
    {
        throw new NotImplementedException();
    }
}