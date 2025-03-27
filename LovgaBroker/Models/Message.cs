namespace LovgaBroker.Models;

public class Message
{
    public string Topic { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}