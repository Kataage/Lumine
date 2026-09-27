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
                source_identity TEXT NOT NULL
                    CHECK(length(source_identity) BETWEEN 18 AND 80),
                raw_width INTEGER NOT NULL CHECK(raw_width > 0),
                raw_height INTEGER NOT NULL CHECK(raw_height > 0),
                has_alpha INTEGER NOT NULL CHECK(has_alpha IN (0, 1)),
                updated_at_utc_ticks INTEGER NOT NULL,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
            );
            """),
        new(
            5,
            "user-metadata-and-local-search",
            """
            CREATE TABLE asset_user_metadata (
                asset_id INTEGER PRIMARY KEY,
                rating INTEGER NULL CHECK(rating IS NULL OR rating BETWEEN 0 AND 5),
                favorite INTEGER NOT NULL DEFAULT 0 CHECK(favorite IN (0, 1)),
                notes TEXT NOT NULL DEFAULT '',
                status_label TEXT NULL CHECK(status_label IS NULL OR length(status_label) <= 64),
                color_label TEXT NULL CHECK(color_label IS NULL OR length(color_label) <= 64),
                updated_at_utc_ticks INTEGER NOT NULL,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
            );

            CREATE TABLE tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 128),
                name_key TEXT NOT NULL CHECK(length(name_key) BETWEEN 1 AND 128),
                created_at_utc_ticks INTEGER NOT NULL,
                UNIQUE(library_id, name_key),
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE
            );

            CREATE TABLE asset_tags (
                asset_id INTEGER NOT NULL,
                tag_id INTEGER NOT NULL,
                created_at_utc_ticks INTEGER NOT NULL,
                PRIMARY KEY(asset_id, tag_id),
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX idx_asset_tags_tag_asset
                ON asset_tags(tag_id, asset_id);

            CREATE TABLE asset_search_documents (
                asset_id INTEGER PRIMARY KEY,
                library_id INTEGER NOT NULL,
                file_name TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT '',
                tags_text TEXT NOT NULL DEFAULT '',
                normalized_text TEXT NOT NULL,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE
            );

            CREATE VIRTUAL TABLE asset_search_fts USING fts5(
                file_name,
                relative_path,
                notes,
                tags_text,
                content='asset_search_documents',
                content_rowid='asset_id',
                tokenize='trigram',
                detail='none',
                columnsize=0
            );

            CREATE TABLE asset_search_cjk_bigrams (
                library_id INTEGER NOT NULL,
                token TEXT NOT NULL CHECK(length(token) = 2),
                asset_id INTEGER NOT NULL,
                PRIMARY KEY(library_id, token, asset_id),
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX idx_asset_search_cjk_asset
                ON asset_search_cjk_bigrams(asset_id);

            CREATE TRIGGER asset_search_documents_ai
            AFTER INSERT ON asset_search_documents
            BEGIN
                INSERT INTO asset_search_fts(
                    rowid, file_name, relative_path, notes, tags_text)
                VALUES (
                    NEW.asset_id, NEW.file_name, NEW.relative_path,
                    NEW.notes, NEW.tags_text);
            END;

            CREATE TRIGGER asset_search_documents_ad
            AFTER DELETE ON asset_search_documents
            BEGIN
                INSERT INTO asset_search_fts(
                    asset_search_fts, rowid,
                    file_name, relative_path, notes, tags_text)
                VALUES (
                    'delete', OLD.asset_id,
                    OLD.file_name, OLD.relative_path,
                    OLD.notes, OLD.tags_text);
            END;

            CREATE TRIGGER asset_search_documents_au
            AFTER UPDATE ON asset_search_documents
            BEGIN
                INSERT INTO asset_search_fts(
                    asset_search_fts, rowid,
                    file_name, relative_path, notes, tags_text)
                VALUES (
                    'delete', OLD.asset_id,
                    OLD.file_name, OLD.relative_path,
                    OLD.notes, OLD.tags_text);

                INSERT INTO asset_search_fts(
                    rowid, file_name, relative_path, notes, tags_text)
                VALUES (
                    NEW.asset_id, NEW.file_name, NEW.relative_path,
                    NEW.notes, NEW.tags_text);
            END;

            CREATE TRIGGER asset_search_documents_ai_cjk
            AFTER INSERT ON asset_search_documents
            WHEN NEW.normalized_text GLOB '*[^ -~]*'
            BEGIN
                INSERT OR IGNORE INTO asset_search_cjk_bigrams(
                    library_id, token, asset_id)
                SELECT
                    NEW.library_id,
                    substr(NEW.normalized_text, position, 2),
                    NEW.asset_id
                FROM (
                    WITH RECURSIVE positions(position) AS (
                        SELECT 1
                        UNION ALL
                        SELECT position + 1
                        FROM positions
                        WHERE position + 1 < length(NEW.normalized_text)
                    )
                    SELECT position FROM positions
                )
                WHERE
                    (
                        unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0x3040 AND 0x30ff
                        OR unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0x3400 AND 0x4dbf
                        OR unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0x4e00 AND 0x9fff
                        OR unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0xff66 AND 0xff9f
                    )
                    AND
                    (
                        unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0x3040 AND 0x30ff
                        OR unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0x3400 AND 0x4dbf
                        OR unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0x4e00 AND 0x9fff
                        OR unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0xff66 AND 0xff9f
                    );
            END;

            CREATE TRIGGER asset_search_documents_au_cjk
            AFTER UPDATE OF normalized_text ON asset_search_documents
            BEGIN
                DELETE FROM asset_search_cjk_bigrams
                WHERE asset_id = NEW.asset_id;

                INSERT OR IGNORE INTO asset_search_cjk_bigrams(
                    library_id, token, asset_id)
                SELECT
                    NEW.library_id,
                    substr(NEW.normalized_text, position, 2),
                    NEW.asset_id
                FROM (
                    WITH RECURSIVE positions(position) AS (
                        SELECT 1
                        UNION ALL
                        SELECT position + 1
                        FROM positions
                        WHERE position + 1 < length(NEW.normalized_text)
                    )
                    SELECT position FROM positions
                )
                WHERE NEW.normalized_text GLOB '*[^ -~]*'
                  AND (
                        unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0x3040 AND 0x30ff
                        OR unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0x3400 AND 0x4dbf
                        OR unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0x4e00 AND 0x9fff
                        OR unicode(substr(NEW.normalized_text, position, 1))
                            BETWEEN 0xff66 AND 0xff9f
                    )
                  AND (
                        unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0x3040 AND 0x30ff
                        OR unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0x3400 AND 0x4dbf
                        OR unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0x4e00 AND 0x9fff
                        OR unicode(substr(NEW.normalized_text, position + 1, 1))
                            BETWEEN 0xff66 AND 0xff9f
                    );
            END;

            CREATE TRIGGER assets_ai_search
            AFTER INSERT ON assets
            BEGIN
                INSERT INTO asset_search_documents(
                    asset_id, library_id, file_name, relative_path,
                    notes, tags_text, normalized_text)
                VALUES(
                    NEW.id, NEW.library_id,
                    NEW.file_name, NEW.relative_path,
                    '', '',
                    lower(NEW.file_name || char(31) || NEW.relative_path));
            END;

            CREATE TRIGGER assets_au_search
            AFTER UPDATE OF file_name, relative_path ON assets
            BEGIN
                UPDATE asset_search_documents
                SET file_name = NEW.file_name,
                    relative_path = NEW.relative_path,
                    normalized_text = lower(
                        NEW.file_name || char(31) ||
                        NEW.relative_path || char(31) ||
                        notes || char(31) ||
                        tags_text)
                WHERE asset_id = NEW.id;
            END;

            INSERT INTO asset_search_documents(
                asset_id, library_id, file_name, relative_path,
                notes, tags_text, normalized_text)
            SELECT
                id, library_id, file_name, relative_path,
                '', '',
                lower(file_name || char(31) || relative_path)
            FROM assets;
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
