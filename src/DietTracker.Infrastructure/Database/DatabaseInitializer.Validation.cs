using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Database;

/// <summary>Pre-migration safety copy and post-migration validation.</summary>
public sealed partial class DatabaseInitializer
{
    private async Task<string?> CreateSafetyCopyAsync(SqliteConnection connection, int fromVersion, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.Combine(_paths.BackupDirectory, "migrations");
            Directory.CreateDirectory(directory);

            var target = Path.Combine(directory, $"diettracker-v{fromVersion}-{DateTime.UtcNow:yyyyMMddHHmmss}.db");

            // VACUUM INTO produces a consistent snapshot including any data still in the WAL file,
            // which a plain file copy would miss.
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $target;";
            command.Parameters.AddWithValue("$target", target);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Safety copy of schema v{Version} created at '{Path}'.", fromVersion, target);
            return target;
        }
        catch (Exception ex)
        {
            // A missing safety copy must not block an update; the migration itself is transactional.
            _logger.LogWarning(ex, "Could not create a pre-migration safety copy.");
            return null;
        }
    }

    private static async Task ValidateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check;";
            var result = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SQLite integrity check failed: {result}");
            }
        }

        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("SQLite foreign key check reported orphaned rows.");
        }
    }
}
