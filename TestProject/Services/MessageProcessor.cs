namespace TestProject.Services;

using LovgaSatellite.Models;

public class MessageProcessor
{
    public void ProcessMessage(ActionModel message)
    {
        Console.WriteLine($"Message from Broker processed: {message.Content}");
    }
}