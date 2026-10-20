using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Database;

/// <summary>Migration discovery and PRAGMA helpers (partial of <see cref="DatabaseInitializer"/>).</summary>
public sealed partial class DatabaseInitializer
{
    private static IReadOnlyList<Migration> LoadMigrations()
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var migrations = new List<Migration>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = VersionPattern().Match(resourceName);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream);

            migrations.Add(new Migration(int.Parse(match.Groups[1].Value), resourceName, reader.ReadToEnd()));
        }

        if (migrations.Count == 0)
        {
            throw new InvalidOperationException("No embedded database migrations were found.");
        }

        return migrations.OrderBy(m => m.Version).ToArray();
    }

    private static IReadOnlyList<Migration> Migrations { get; } = LoadMigrations();

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return SqliteValue.GetInt(value);
    }

    private static async Task SetUserVersionAsync(SqliteConnection connection, SqliteTransaction? transaction, int version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // PRAGMA does not accept parameters, hence the (integer validated) interpolation.
        command.CommandText = $"PRAGMA user_version = {version};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"(\d{4})_[^\\/]+\.sql$")]
    private static partial Regex VersionPattern();

    private sealed record Migration(int Version, string Name, string Sql);
}

/// <summary>
/// Thrown when the database file was created by a newer build of DietTracker. The application
/// refuses to open it instead of risking data loss.
/// </summary>
public sealed class DatabaseSchemaTooNewException : Exception
{
    public DatabaseSchemaTooNewException(int databaseVersion, int supportedVersion, string path)
        : base($"The database at '{path}' uses schema v{databaseVersion} but this build supports v{supportedVersion}. Update DietTracker to continue.")
    {
        DatabaseVersion = databaseVersion;
        SupportedVersion = supportedVersion;
    }

    public int DatabaseVersion { get; }

    public int SupportedVersion { get; }
}
