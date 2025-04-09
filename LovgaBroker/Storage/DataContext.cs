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
        return new SqliteConnection(_configuration.GetConnectionString("Storage"));
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
  CreatedAt INTEGER
);
";
        connection.Execute(sql);
    }
}