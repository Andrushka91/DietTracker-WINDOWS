// DietTracker database inspector / exporter.
//
//   DbInspector inspect                       schema + row counts
//   DbInspector export                        csv per table + postgresql dump + a safe copy of the db
//
// The tool always works on a COPY of the database (with its -wal file) so it never interferes with
// a running DietTracker instance.

using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

var defaultDb = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DietTracker", "database", "diettracker.db");

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "inspect";
var source = GetOption(args, "--source") ?? defaultDb;
var output = GetOption(args, "--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "db-export");

if (!File.Exists(source))
{
    Console.Error.WriteLine($"Database not found: {source}");
    return 1;
}

Directory.CreateDirectory(output);
var snapshot = Snapshot(source, output);
Console.WriteLine($"Source   : {source}");
Console.WriteLine($"Snapshot : {snapshot}  (copy, safe to read while the app runs)");
Console.WriteLine();

using var connection = new SqliteConnection($"Data Source={snapshot};Mode=ReadWrite");
connection.Open();

var tables = new List<string>();
using (var cmd = connection.CreateCommand())
{
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        tables.Add(reader.GetString(0));
    }
}

if (command == "inspect")
{
    Console.WriteLine("table                      rows");
    Console.WriteLine("-------------------------- -----");
    foreach (var table in tables)
    {
        Console.WriteLine($"{table,-26} {Count(connection, table),5}");
    }

    Console.WriteLine();
    Console.WriteLine("schema (first 40 columns of every table):");
    foreach (var table in tables)
    {
        Console.WriteLine($"\n{table}:");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{table}');";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"  {reader.GetString(1),-24} {reader.GetString(2)}");
        }
    }

    return 0;
}

if (command != "export")
{
    Console.Error.WriteLine("Usage: DbInspector [inspect|export] [--source <path>] [--out <dir>]");
    return 2;
}

// ---- export: csv per table + a PostgreSQL compatible dump ----
var postgres = new StringBuilder();
postgres.AppendLine("-- DietTracker export -> PostgreSQL");
postgres.AppendLine("-- Restore with:  psql -U <user> -d <database> -f diettracker-postgres.sql");
postgres.AppendLine("-- (or open the file in pgAdmin's Query tool and execute it)");
postgres.AppendLine("-- Values that were stored as ISO-8601 text in SQLite are cast to TIMESTAMPTZ / DATE / TIME.");
postgres.AppendLine("SET client_encoding = 'UTF8';");
postgres.AppendLine("BEGIN;");

foreach (var table in tables)
{
    var columns = Columns(connection, table);

    var create = new StringBuilder();
    create.AppendLine($"CREATE TABLE IF NOT EXISTS {Quote(table)} (");
    for (var i = 0; i < columns.Count; i++)
    {
        var (name, type) = columns[i];
        create.Append($"    {Quote(name)} {PostgresType(name, type)}");
        create.AppendLine(i + 1 < columns.Count ? "," : string.Empty);
    }

    create.AppendLine(");");
    postgres.AppendLine(create.ToString());

    var csv = new StringBuilder();
    csv.AppendLine(string.Join(",", columns.Select(c => Csv(c.name))));

    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = $"SELECT {string.Join(",", columns.Select(c => Quote(c.name)))} FROM {Quote(table)};";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var values = new List<string>();
            var literals = new List<string>();
            for (var i = 0; i < columns.Count; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                values.Add(value is null ? string.Empty : Csv(value));
                literals.Add(PostgresLiteral(columns[i], value));
            }

            csv.AppendLine(string.Join(",", values));
            postgres.AppendLine($"INSERT INTO {Quote(table)} ({string.Join(",", columns.Select(c => Quote(c.name)))}) VALUES ({string.Join(",", literals)});");
        }
    }

    File.WriteAllText(Path.Combine(output, $"{table}.csv"), csv.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"wrote {table,-26} {Count(connection, table),5} rows  -> {table}.csv");
}

postgres.AppendLine("COMMIT;");
var sqlPath = Path.Combine(output, "diettracker-postgres.sql");
File.WriteAllText(sqlPath, postgres.ToString(), new UTF8Encoding(false));
Console.WriteLine();
Console.WriteLine($"PostgreSQL dump : {sqlPath}");
Console.WriteLine($"CSV folder      : {output}");
return 0;

// ---------------- helpers ----------------

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string Snapshot(string source, string output)
{
    // Copy the database together with its WAL/SHM files, then checkpoint the copy so the exported
    // snapshot is consistent and can be opened by any SQLite tool.
    var target = Path.Combine(output, "diettracker-snapshot.db");
    foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
    {
        var from = source + suffix;
        if (File.Exists(from))
        {
            File.Copy(from, target + suffix, overwrite: true);
        }
    }

    using (var connection = new SqliteConnection($"Data Source={target};Mode=ReadWrite"))
    {
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.ExecuteNonQuery();
    }

    // The connection must be closed before the sidecar files can be removed.
    try
    {
        File.Delete(target + "-wal");
        File.Delete(target + "-shm");
    }
    catch (IOException)
    {
        // Harmless: the snapshot is still valid with its WAL present.
    }

    return target;
}

static int Count(SqliteConnection connection, string table)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)};";
    return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
}

static List<(string name, string type)> Columns(SqliteConnection connection, string table)
{
    var columns = new List<(string, string)>();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = $"PRAGMA table_info('{table}');";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        columns.Add((reader.GetString(1), reader.IsDBNull(2) ? "TEXT" : reader.GetString(2)));
    }

    return columns;
}

static string PostgresType(string name, string sqliteType)
{
    // Name based refinement: the application stores ISO-8601 text, so expose real date/time types.
    if (name.EndsWith("At", StringComparison.Ordinal))
    {
        return "TIMESTAMPTZ";
    }

    if (name.Equals("Date", StringComparison.OrdinalIgnoreCase))
    {
        return "DATE";
    }

    if (name.EndsWith("Time", StringComparison.OrdinalIgnoreCase))
    {
        return "TIME";
    }

    return sqliteType.ToUpperInvariant() switch
    {
        "INTEGER" or "INT" or "BOOLEAN" or "BOOL" => "BIGINT",
        "REAL" or "DOUBLE" or "FLOAT" => "DOUBLE PRECISION",
        "BLOB" => "BYTEA",
        "NUMERIC" or "DECIMAL" => "NUMERIC",
        _ => "TEXT",
    };
}

static string PostgresLiteral((string name, string type) column, string? value)
{
    if (value is null)
    {
        return "NULL";
    }

    var pgType = PostgresType(column.name, column.type);
    if (pgType is "BIGINT" or "DOUBLE PRECISION" or "NUMERIC")
    {
        return long.TryParse(value, out _) || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? value
            : "NULL";
    }

    return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'::{pgType}";
}

static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

static string Csv(string value)
    => value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
