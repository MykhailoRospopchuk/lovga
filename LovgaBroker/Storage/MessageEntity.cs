namespace LovgaBroker.Storage;

public class MessageEntity
{
    public int Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
}