using Microsoft.Data.Sqlite;
using Freely.Storage.Database;

namespace Freely.Storage.Repositories;

public sealed record ConversationSummary(string Id, string Title, DateTimeOffset UpdatedUtc);
public sealed record StoredMessage(string Id, string Role, string Content, DateTimeOffset CreatedUtc);

public sealed class ConversationRepository(FreelyDatabase database)
{
    public async Task<string> CreateAsync(string firstMessage, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var title = firstMessage.Trim().ReplaceLineEndings(" ");
        if (title.Length > 54) title = title[..54] + "…";

        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO conversations(id, title, created_utc, updated_utc) VALUES ($id, $title, $now, $now);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task AddMessageAsync(string conversationId, string role, string content, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = database.OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var message = connection.CreateCommand())
        {
            message.Transaction = (SqliteTransaction)transaction;
            message.CommandText = "INSERT INTO messages(id, conversation_id, role, content, created_utc) VALUES ($id, $conversation, $role, $content, $now);";
            message.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            message.Parameters.AddWithValue("$conversation", conversationId);
            message.Parameters.AddWithValue("$role", role);
            message.Parameters.AddWithValue("$content", content);
            message.Parameters.AddWithValue("$now", now.ToString("O"));
            await message.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var conversation = connection.CreateCommand())
        {
            conversation.Transaction = (SqliteTransaction)transaction;
            conversation.CommandText = "UPDATE conversations SET updated_utc = $now WHERE id = $id;";
            conversation.Parameters.AddWithValue("$now", now.ToString("O"));
            conversation.Parameters.AddWithValue("$id", conversationId);
            await conversation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var items = new List<ConversationSummary>();
        await using var connection = database.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, updated_utc FROM conversations ORDER BY updated_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ConversationSummary(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))));
        }

        return items;
    }
}

