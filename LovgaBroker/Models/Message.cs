namespace LovgaBroker.Models;

public class Message
{
    public string Topic { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}