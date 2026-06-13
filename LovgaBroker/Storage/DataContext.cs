namespace LovgaBroker.Storage;

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

public class DataContext
{
    private readonly IConfiguration _configuration;

    public DataContext(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IDbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_configuration.GetConnectionString("Storage"));
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA temp_store=MEMORY;
                PRAGMA busy_timeout=5000;
            ";
            command.ExecuteNonQuery();
        }
        return connection;
    }

    public void Init()
    {
        using var connection = CreateConnection();

        var sql = 
@"
CREATE TABLE IF NOT EXISTS 
MessageEntity (
  Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  Topic TEXT,
  Content TEXT,
  CreatedAt INTEGER,
  InFlight INTEGER DEFAULT 0,
  LastDeliveredAt INTEGER DEFAULT 0
);
";
        connection.Execute(sql);
    }
}