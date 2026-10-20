using DietTracker.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Database;

/// <summary>Creates configured SQLite connections for one database file.</summary>
public interface ISqliteConnectionFactory
{
    /// <summary>Full path of the database file.</summary>
    string DatabasePath { get; }

    /// <summary>Opens a connection with the required pragmas already applied.</summary>
    Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default connection factory. Every connection enables foreign keys, WAL journaling (better
/// concurrency for the background synchronization) and a busy timeout so that a concurrent
/// reader never fails immediately.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureCreated();

        DatabasePath = paths.DatabaseFilePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            ForeignKeys = true,
        }.ToString();
    }

    /// <inheritdoc />
    public string DatabasePath { get; }

    /// <inheritdoc />
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA busy_timeout = 5000;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
