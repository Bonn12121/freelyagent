using Freely.Storage.Database;

namespace Freely.Storage.Repositories;

public sealed class SettingsRepository(FreelyDatabase database)
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value, updated_utc) VALUES ($key, $value, $now)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
