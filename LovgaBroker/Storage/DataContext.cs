namespace LovgaBroker.Storage;

using System.Data;
using System.IO;
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
        var connectionString = _configuration.GetConnectionString("Storage");
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = "Data Source=../.storage_database/StorageDatabase.db";
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        builder.DataSource = ResolveDatabasePath(builder.DataSource);

        var connection = new SqliteConnection(builder.ConnectionString);
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

    private static string ResolveDatabasePath(string dataSource)
    {
        if (string.IsNullOrEmpty(dataSource) || Path.IsPathRooted(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return dataSource;
        }

        // 1. Try CWD first (e.g. inside Docker, CWD=/app, and path resolves to target directory if it exists)
        var cwdPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), dataSource));
        var cwdDir = Path.GetDirectoryName(cwdPath);
        if (cwdDir != null && Directory.Exists(cwdDir))
        {
            return cwdPath;
        }

        // 2. Traverse up from AppContext.BaseDirectory to locate the workspace root containing 'stress-test.sh' or '.git'
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "stress-test.sh")) || Directory.Exists(Path.Combine(dir, ".git")))
            {
                var resolvedPath = Path.GetFullPath(Path.Combine(dir, ".storage_database", Path.GetFileName(dataSource)));
                var resolvedDir = Path.GetDirectoryName(resolvedPath);
                if (resolvedDir != null)
                {
                    Directory.CreateDirectory(resolvedDir);
                }
                return resolvedPath;
            }
            dir = Path.GetDirectoryName(dir);
        }

        // 3. Fallback: Create parent directory relative to CWD if not found
        if (cwdDir != null)
        {
            Directory.CreateDirectory(cwdDir);
        }
        return cwdPath;
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