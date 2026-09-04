using Microsoft.Data.Sqlite;

namespace Freely.Storage.Database;

public sealed class FreelyDatabase
{
    private readonly string _connectionString;

    public FreelyDatabase(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_messages_conversation_created
                ON messages(conversation_id, created_utc);

            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                conversation_id TEXT,
                goal TEXT NOT NULL,
                status TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS permissions (
                tool_name TEXT PRIMARY KEY,
                level TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tool_calls (
                id TEXT PRIMARY KEY,
                task_id TEXT,
                tool_name TEXT NOT NULL,
                arguments_json TEXT NOT NULL,
                success INTEGER,
                output TEXT,
                created_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

