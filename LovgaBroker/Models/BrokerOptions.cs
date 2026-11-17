namespace LovgaBroker.Models;

public sealed class BrokerOptions
{
    public const string SectionName = "Broker";

    /// <summary>
    /// When true (default), messages are persisted to SQLite before delivery.
    /// When false, messages bypass the database and are dispatched directly to consumers.
    /// </summary>
    public bool PersistMessages { get; set; } = true;
}
