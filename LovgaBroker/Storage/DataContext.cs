namespace LovgaBroker.Storage;

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

public class DataContext
{
    protected readonly IConfiguration Configuration;

    public DataContext(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IDbConnection CreateConnection()
    {
        return new SqliteConnection(Configuration.GetConnectionString("Storage"));
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