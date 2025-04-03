namespace TestProject.Services;

using LovgaSatellite.Models;

public class MessageProcessor
{
    public static void ProcessMessage(ActionModel message)
    {
        Console.WriteLine($"Message from Broker processed: {message.Content}");
    }
}