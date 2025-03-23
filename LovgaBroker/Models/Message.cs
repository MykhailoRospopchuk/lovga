namespace LovgaBroker.Models;

public class Message
{
    public string Topic { get; set; }
    public string Content { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}