using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed class LibraryDatabase
{
    private static readonly Migration[] Migrations =
    [
        new(
            1,
            "initial-library-core",
            """
            CREATE TABLE libraries (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL,
                root_path_key TEXT NOT NULL UNIQUE,
                created_at_utc_ticks INTEGER NOT NULL,
                updated_at_utc_ticks INTEGER NOT NULL,
                last_scan_completed_at_utc_ticks INTEGER NULL
            );

            CREATE TABLE folders (
                id INTEGER PRIMARY KEY,
                library_id INTEGER NOT NULL,
                relative_path TEXT NOT NULL,
                relative_path_key TEXT NOT NULL,
                created_at_utc_ticks INTEGER NOT NULL,
                UNIQUE(library_id, relative_path_key),
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE
            );

            CREATE TABLE assets (
                id INTEGER PRIMARY KEY,
                asset_key TEXT NOT NULL UNIQUE,
                library_id INTEGER NOT NULL,
                folder_id INTEGER NULL,
                relative_path TEXT NOT NULL,
                relative_path_key TEXT NOT NULL,
                file_name TEXT NOT NULL,
                extension TEXT NOT NULL,
                file_size INTEGER NOT NULL CHECK(file_size >= 0),
                modified_at_utc_ticks INTEGER NOT NULL,
                width INTEGER NULL CHECK(width IS NULL OR width > 0),
                height INTEGER NULL CHECK(height IS NULL OR height > 0),
                format TEXT NULL,
                created_at_utc_ticks INTEGER NOT NULL,
                updated_at_utc_ticks INTEGER NOT NULL,
                UNIQUE(library_id, relative_path_key),
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE,
                FOREIGN KEY(folder_id) REFERENCES folders(id) ON DELETE SET NULL
            );

            CREATE INDEX idx_assets_library_modified_id
                ON assets(library_id, modified_at_utc_ticks DESC, id DESC);

            CREATE INDEX idx_assets_library_folder
                ON assets(library_id, folder_id);
            """)
    ];

    public LibraryDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecutePragmasAsync(
            connection,
            """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            """,
            cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at_utc_ticks INTEGER NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var appliedVersion = 0;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            appliedVersion = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }

        foreach (var migration in Migrations)
        {
            if (migration.Version <= appliedVersion)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO schema_migrations(version, name, applied_at_utc_ticks)
                    VALUES ($version, $name, $applied);
                    """;
                command.Parameters.AddWithValue("$version", migration.Version);
                command.Parameters.AddWithValue("$name", migration.Name);
                command.Parameters.AddWithValue("$applied", DateTimeOffset.UtcNow.UtcDateTime.Ticks);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
    }

    internal async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecutePragmasAsync(
            connection,
            """
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            PRAGMA synchronous=NORMAL;
            """,
            cancellationToken).ConfigureAwait(false);

        return connection;
    }

    public static void ClearPools() => SqliteConnection.ClearAllPools();

    private static async Task ExecutePragmasAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record Migration(int Version, string Name, string Sql);
}
