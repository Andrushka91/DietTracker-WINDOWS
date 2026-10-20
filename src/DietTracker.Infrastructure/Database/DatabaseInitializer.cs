using DietTracker.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Database;

/// <summary>
/// Applies versioned, transactional SQLite migrations from embedded SQL scripts and keeps
/// <c>PRAGMA user_version</c> in sync with the schema.
/// </summary>
/// <remarks>
/// Guarantees required by the Microsoft Store update path: the current schema version is detected
/// before anything changes; a consistent safety copy is produced with <c>VACUUM INTO</c> before an
/// upgrade; each migration runs inside its own transaction so a failure leaves the previous version
/// intact; and the resulting database is validated (<c>integrity_check</c> + <c>foreign_key_check</c>).
/// </remarks>
public sealed partial class DatabaseInitializer : IDatabaseInitializer
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly IAppDataPaths _paths;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ISqliteConnectionFactory factory, IAppDataPaths paths, ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public int TargetSchemaVersion => Migrations.Max(m => m.Version);

    /// <inheritdoc />
    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_factory.DatabasePath))
        {
            return 0;
        }

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetUserVersionAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();

        var target = TargetSchemaVersion;
        var existedBefore = File.Exists(_factory.DatabasePath) && new FileInfo(_factory.DatabasePath).Length > 0;

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var current = await GetUserVersionAsync(connection, null, cancellationToken).ConfigureAwait(false);

        if (current > target)
        {
            throw new DatabaseSchemaTooNewException(current, target, _factory.DatabasePath);
        }

        string? safetyCopy = null;
        if (existedBefore && current > 0 && current < target)
        {
            safetyCopy = await CreateSafetyCopyAsync(connection, current, cancellationToken).ConfigureAwait(false);
        }

        var applied = new List<string>();

        foreach (var migration in Migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            _logger.LogInformation("Applying database migration {Migration}.", migration.Name);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await SetUserVersionAsync(connection, transaction, migration.Version, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            applied.Add(migration.Name);
        }

        await ValidateAsync(connection, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Database ready at '{Path}' (schema {From} -> {To}, {Count} migration(s) applied).",
            _factory.DatabasePath, current, target, applied.Count);

        return new DatabaseInitializationResult
        {
            FromVersion = current,
            ToVersion = target,
            AppliedMigrations = applied,
            DatabasePath = _factory.DatabasePath,
            BackupPath = safetyCopy,
        };
    }
}
