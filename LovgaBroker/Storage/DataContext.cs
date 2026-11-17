namespace LovgaBroker.Storage;

using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

public sealed class DataContext
{
    private readonly string _connectionString;

    public DataContext(IConfiguration configuration)
    {
        var rawConnectionString = configuration.GetConnectionString("Storage");
        if (string.IsNullOrEmpty(rawConnectionString))
        {
            rawConnectionString = "Data Source=../.storage_database/StorageDatabase.db";
        }

        var builder = new SqliteConnectionStringBuilder(rawConnectionString)
        {
            Pooling = true,
            DefaultTimeout = 30
        };
        builder.DataSource = ResolveDatabasePath(builder.DataSource);
        _connectionString = builder.ConnectionString;
    }

    public IDbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
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

        const string sql = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;
            PRAGMA busy_timeout=30000;

            CREATE TABLE IF NOT EXISTS MessageEntity (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
              Topic TEXT NOT NULL,
              Content TEXT NOT NULL,
              CreatedAt INTEGER NOT NULL,
              InFlight INTEGER DEFAULT 0,
              LastDeliveredAt INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_MessageEntity_InFlight_Id ON MessageEntity (InFlight, Id);
            """;

        connection.Execute(sql);
    }
}