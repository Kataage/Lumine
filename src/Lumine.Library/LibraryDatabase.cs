using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed class LibrarySchemaException : InvalidOperationException
{
    public LibrarySchemaException(string message) : base(message)
    {
    }
}

public sealed class LibraryDatabaseCompatibilityException : InvalidOperationException
{
    public LibraryDatabaseCompatibilityException(string message) : base(message)
    {
    }
}

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
            """),
        new(
            2,
            "harden-library-core-invariants",
            """
            ALTER TABLE libraries
                ADD COLUMN scan_state INTEGER NOT NULL DEFAULT 0;

            ALTER TABLE libraries
                ADD COLUMN last_scan_attempted_at_utc_ticks INTEGER NULL;

            UPDATE libraries
            SET scan_state = 2,
                last_scan_attempted_at_utc_ticks = last_scan_completed_at_utc_ticks
            WHERE last_scan_completed_at_utc_ticks IS NOT NULL;

            DROP INDEX IF EXISTS idx_assets_library_modified_id;
            DROP INDEX IF EXISTS idx_assets_library_folder;

            ALTER TABLE assets RENAME TO assets_v1;

            CREATE TABLE assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                folder_id INTEGER NULL,
                relative_path TEXT NOT NULL,
                relative_path_key TEXT NOT NULL,
                file_name TEXT NOT NULL,
                extension TEXT NOT NULL,
                file_size INTEGER NOT NULL CHECK(file_size >= 0),
                modified_at_utc_ticks INTEGER NOT NULL,
                source_revision INTEGER NOT NULL DEFAULT 1 CHECK(source_revision > 0),
                width INTEGER NULL CHECK(width IS NULL OR width > 0),
                height INTEGER NULL CHECK(height IS NULL OR height > 0),
                format TEXT NULL CHECK(format IS NULL OR length(format) <= 64),
                created_at_utc_ticks INTEGER NOT NULL,
                updated_at_utc_ticks INTEGER NOT NULL,
                UNIQUE(library_id, relative_path_key),
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE,
                FOREIGN KEY(folder_id) REFERENCES folders(id) ON DELETE SET NULL
            );

            INSERT INTO assets(
                id, library_id, folder_id,
                relative_path, relative_path_key,
                file_name, extension,
                file_size, modified_at_utc_ticks,
                source_revision,
                width, height, format,
                created_at_utc_ticks, updated_at_utc_ticks)
            SELECT
                id, library_id, folder_id,
                relative_path, relative_path_key,
                file_name, extension,
                file_size, modified_at_utc_ticks,
                1,
                width, height, format,
                created_at_utc_ticks, updated_at_utc_ticks
            FROM assets_v1;

            DROP TABLE assets_v1;

            CREATE INDEX idx_assets_library_modified_id
                ON assets(library_id, modified_at_utc_ticks DESC, id DESC);

            CREATE INDEX idx_assets_library_folder
                ON assets(library_id, folder_id);
            """),
        new(
            3,
            "incremental-filesystem-sync",
            """
            ALTER TABLE assets
                ADD COLUMN observed_generation INTEGER NOT NULL DEFAULT 0;

            CREATE TABLE library_sync_state (
                library_id INTEGER PRIMARY KEY,
                reconcile_generation INTEGER NOT NULL DEFAULT 0,
                reconcile_required INTEGER NOT NULL DEFAULT 1,
                usn_journal_id TEXT NULL,
                next_usn INTEGER NULL,
                watcher_stopped_at_utc_ticks INTEGER NULL,
                last_reconciled_at_utc_ticks INTEGER NULL,
                last_error TEXT NULL,
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE
            );

            INSERT INTO library_sync_state(library_id)
            SELECT id FROM libraries;
            """),
        new(
            4,
            "persist-source-technical-metadata",
            """
            CREATE TABLE asset_technical_metadata (
                asset_id INTEGER PRIMARY KEY,
                source_revision INTEGER NOT NULL CHECK(source_revision > 0),
                source_content_sha256 TEXT NOT NULL
                    CHECK(length(source_content_sha256) = 64),
                raw_width INTEGER NOT NULL CHECK(raw_width > 0),
                raw_height INTEGER NOT NULL CHECK(raw_height > 0),
                has_alpha INTEGER NOT NULL CHECK(has_alpha IN (0, 1)),
                updated_at_utc_ticks INTEGER NOT NULL,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
            );
            """)
    ];

    public LibraryDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public static int SupportedSchemaVersion => Migrations[^1].Version;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var migrationTableExists = await HasMigrationTableAsync(
            connection,
            cancellationToken).ConfigureAwait(false);

        var applied = migrationTableExists
            ? await ReadMigrationHistoryAsync(connection, cancellationToken).ConfigureAwait(false)
            : [];

        if (migrationTableExists)
        {
            ValidateMigrationHistory(applied);
        }

        if (applied.Count == 0
            && await HasProductTablesAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            throw new LibrarySchemaException(
                "Library database contains product tables but no migration history. Refusing to guess the schema.");
        }

        await EnsureWalModeAsync(connection, cancellationToken).ConfigureAwait(false);

        await ExecutePragmasAsync(
            connection,
            """
            PRAGMA synchronous=NORMAL;
            PRAGMA temp_store=MEMORY;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            """,
            cancellationToken).ConfigureAwait(false);

        if (!migrationTableExists)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at_utc_ticks INTEGER NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var appliedVersion = applied.Count == 0 ? 0 : applied[^1].Version;

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
            appliedVersion = migration.Version;
        }

        var finalHistory = await ReadMigrationHistoryAsync(connection, cancellationToken).ConfigureAwait(false);
        ValidateMigrationHistory(finalHistory);

        if (finalHistory.Count != Migrations.Length)
        {
            throw new LibrarySchemaException(
                $"Library schema initialization ended at version {appliedVersion}, expected {SupportedSchemaVersion}.");
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

    private static async Task EnsureWalModeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";

        var value = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        if (!string.Equals(value, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new LibraryDatabaseCompatibilityException(
                $"Library database requires WAL journal mode, but SQLite reported '{value ?? "<null>"}'.");
        }
    }

    private static async Task<List<AppliedMigration>> ReadMigrationHistoryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<AppliedMigration>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT version, name
            FROM schema_migrations
            ORDER BY version ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AppliedMigration(reader.GetInt32(0), reader.GetString(1)));
        }

        return result;
    }

    private static void ValidateMigrationHistory(IReadOnlyList<AppliedMigration> applied)
    {
        for (var index = 0; index < applied.Count; index++)
        {
            var row = applied[index];
            var expectedVersion = index + 1;

            if (row.Version != expectedVersion)
            {
                throw new LibrarySchemaException(
                    $"Library migration history has a gap or unexpected version. Expected {expectedVersion}, found {row.Version}.");
            }

            if (row.Version > SupportedSchemaVersion)
            {
                throw new LibrarySchemaException(
                    $"Library database schema version {row.Version} is newer than supported version {SupportedSchemaVersion}.");
            }

            var expected = Migrations[index];
            if (!string.Equals(row.Name, expected.Name, StringComparison.Ordinal))
            {
                throw new LibrarySchemaException(
                    $"Library migration {row.Version} name mismatch. Expected '{expected.Name}', found '{row.Name}'.");
            }
        }
    }

    private static async Task<bool> HasMigrationTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'schema_migrations'
            );
            """;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<bool> HasProductTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('libraries', 'folders', 'assets')
            );
            """;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

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

    private sealed record AppliedMigration(int Version, string Name);
}
