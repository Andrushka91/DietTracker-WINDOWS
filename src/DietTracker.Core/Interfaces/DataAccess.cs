namespace DietTracker.Core.Interfaces;

/// <summary>
/// Versioned, transactional database migration runner. Migrations are embedded SQL scripts applied
/// in ascending order and recorded in <c>PRAGMA user_version</c>.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>Applies all pending migrations; safe to call on every startup and idempotent.</summary>
    Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Current schema version of the database file (0 when the file does not exist yet).</summary>
    Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Expected schema version of this build.</summary>
    int TargetSchemaVersion { get; }
}

/// <summary>Outcome of database initialization.</summary>
public sealed record DatabaseInitializationResult
{
    public required int FromVersion { get; init; }

    public required int ToVersion { get; init; }

    public required IReadOnlyList<string> AppliedMigrations { get; init; }

    public required string DatabasePath { get; init; }

    public bool Upgraded => ToVersion > FromVersion;

    /// <summary>Path of the automatic pre-migration safety copy, when one was created.</summary>
    public string? BackupPath { get; init; }
}

/// <summary>Runs logical integrity checks on the local database.</summary>
public interface IDataIntegrityChecker
{
    Task<IReadOnlyList<Models.DataIntegrityIssue>> CheckAsync(CancellationToken cancellationToken = default);
}
