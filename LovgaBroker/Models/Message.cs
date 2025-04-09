namespace LovgaBroker.Models;

public class Message
{
    public int Id { get; private set; }
    public string Topic { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public void SetId(int id)
    {
        Id = id;
    }
}