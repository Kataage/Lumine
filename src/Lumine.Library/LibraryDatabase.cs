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

            -- Search is a rebuildable derived index. Do not duplicate source text
            -- in a content table and do not put FTS work on the asset ingest path.
            CREATE VIRTUAL TABLE asset_search_fts USING fts5(
                file_name,
                relative_path,
                notes,
                tags_text,
                content='',
                contentless_delete=1,
                tokenize='trigram',
                detail='none'
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

            CREATE TABLE asset_search_dirty (
                asset_id INTEGER PRIMARY KEY,
                library_id INTEGER NOT NULL,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE
            );

            CREATE INDEX idx_asset_search_dirty_library
                ON asset_search_dirty(library_id, asset_id);

            CREATE TRIGGER assets_ai_search_dirty
            AFTER INSERT ON assets
            BEGIN
                INSERT INTO asset_search_dirty(asset_id, library_id)
                VALUES(NEW.id, NEW.library_id)
                ON CONFLICT(asset_id) DO UPDATE SET
                    library_id = excluded.library_id;
            END;

            CREATE TRIGGER assets_au_search_dirty
            AFTER UPDATE OF file_name, relative_path ON assets
            WHEN NEW.file_name <> OLD.file_name
              OR NEW.relative_path <> OLD.relative_path
            BEGIN
                INSERT INTO asset_search_dirty(asset_id, library_id)
                VALUES(NEW.id, NEW.library_id)
                ON CONFLICT(asset_id) DO UPDATE SET
                    library_id = excluded.library_id;
            END;

            CREATE TRIGGER assets_bd_search_cleanup
            BEFORE DELETE ON assets
            BEGIN
                DELETE FROM asset_search_fts
                WHERE rowid = OLD.id;
            END;

            -- Existing v1-v4 assets are deliberately marked dirty instead of
            -- synchronously building an FTS index during schema migration.
            INSERT INTO asset_search_dirty(asset_id, library_id)
            SELECT id, library_id
            FROM assets;
            """),
        new(
            6,
            "product-navigation-library-state",
            """
            ALTER TABLE libraries
                ADD COLUMN is_enabled INTEGER NOT NULL DEFAULT 1
                    CHECK(is_enabled IN (0, 1));

            CREATE INDEX idx_libraries_enabled_updated
                ON libraries(is_enabled DESC, updated_at_utc_ticks DESC, id ASC);
            """),
        new(
            7,
            "creative-archive-domain",
            """
            CREATE TABLE works (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                title TEXT NOT NULL CHECK(length(title) BETWEEN 1 AND 256),
                description TEXT NOT NULL DEFAULT '',
                cover_asset_id INTEGER NULL,
                created_at_utc_ticks INTEGER NOT NULL,
                updated_at_utc_ticks INTEGER NOT NULL,
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE,
                FOREIGN KEY(cover_asset_id) REFERENCES assets(id) ON DELETE SET NULL
            );

            CREATE TABLE work_assets (
                work_id INTEGER NOT NULL,
                asset_id INTEGER NOT NULL,
                sort_order INTEGER NOT NULL CHECK(sort_order >= 0),
                role TEXT NOT NULL DEFAULT 'member'
                    CHECK(length(role) BETWEEN 1 AND 64),
                PRIMARY KEY(work_id, asset_id),
                UNIQUE(work_id, sort_order),
                FOREIGN KEY(work_id) REFERENCES works(id) ON DELETE CASCADE,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX idx_work_assets_asset
                ON work_assets(asset_id, work_id);

            CREATE TABLE generation_groups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                work_id INTEGER NULL,
                name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 256),
                prompt TEXT NOT NULL DEFAULT '',
                negative_prompt TEXT NOT NULL DEFAULT '',
                model_name TEXT NOT NULL DEFAULT '',
                sampler TEXT NOT NULL DEFAULT '',
                scheduler TEXT NOT NULL DEFAULT '',
                steps INTEGER NOT NULL DEFAULT 0 CHECK(steps >= 0),
                cfg_scale REAL NOT NULL DEFAULT 0 CHECK(cfg_scale >= 0),
                workflow_json TEXT NOT NULL DEFAULT '',
                notes TEXT NOT NULL DEFAULT '',
                created_at_utc_ticks INTEGER NOT NULL,
                updated_at_utc_ticks INTEGER NOT NULL,
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE,
                FOREIGN KEY(work_id) REFERENCES works(id) ON DELETE SET NULL
            );

            CREATE INDEX idx_generation_groups_library_updated
                ON generation_groups(library_id, updated_at_utc_ticks DESC, id DESC);

            CREATE INDEX idx_generation_groups_work
                ON generation_groups(work_id, id);

            CREATE TABLE generation_group_assets (
                generation_group_id INTEGER NOT NULL,
                asset_id INTEGER NOT NULL,
                sort_order INTEGER NOT NULL CHECK(sort_order >= 0),
                is_primary INTEGER NOT NULL DEFAULT 0
                    CHECK(is_primary IN (0, 1)),
                PRIMARY KEY(generation_group_id, asset_id),
                UNIQUE(generation_group_id, sort_order),
                FOREIGN KEY(generation_group_id)
                    REFERENCES generation_groups(id) ON DELETE CASCADE,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX idx_generation_group_assets_asset
                ON generation_group_assets(asset_id, generation_group_id);

            CREATE TABLE asset_relations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                parent_asset_id INTEGER NOT NULL,
                child_asset_id INTEGER NOT NULL,
                relation_type TEXT NOT NULL
                    CHECK(length(relation_type) BETWEEN 1 AND 64),
                note TEXT NOT NULL DEFAULT '',
                created_at_utc_ticks INTEGER NOT NULL,
                CHECK(parent_asset_id <> child_asset_id),
                UNIQUE(parent_asset_id, child_asset_id, relation_type),
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE,
                FOREIGN KEY(parent_asset_id) REFERENCES assets(id) ON DELETE CASCADE,
                FOREIGN KEY(child_asset_id) REFERENCES assets(id) ON DELETE CASCADE
            );

            CREATE INDEX idx_asset_relations_parent
                ON asset_relations(parent_asset_id, created_at_utc_ticks DESC);

            CREATE INDEX idx_asset_relations_child
                ON asset_relations(child_asset_id, created_at_utc_ticks DESC);

            CREATE TABLE publications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_id INTEGER NOT NULL,
                work_id INTEGER NULL,
                title TEXT NOT NULL DEFAULT '',
                body TEXT NOT NULL DEFAULT '',
                tags_snapshot TEXT NOT NULL DEFAULT '',
                destination TEXT NOT NULL
                    CHECK(length(destination) BETWEEN 1 AND 128),
                account TEXT NOT NULL DEFAULT '',
                published_at_utc_ticks INTEGER NOT NULL,
                external_id TEXT NOT NULL DEFAULT '',
                external_url TEXT NOT NULL DEFAULT '',
                platform_metadata_json TEXT NOT NULL DEFAULT '{}',
                created_at_utc_ticks INTEGER NOT NULL,
                updated_at_utc_ticks INTEGER NOT NULL,
                FOREIGN KEY(library_id) REFERENCES libraries(id) ON DELETE CASCADE,
                FOREIGN KEY(work_id) REFERENCES works(id) ON DELETE SET NULL
            );

            CREATE INDEX idx_publications_library_published
                ON publications(
                    library_id,
                    published_at_utc_ticks DESC,
                    id DESC);

            CREATE INDEX idx_publications_work
                ON publications(work_id, published_at_utc_ticks DESC);

            CREATE TABLE publication_assets (
                publication_id INTEGER NOT NULL,
                asset_id INTEGER NULL,
                sort_order INTEGER NOT NULL CHECK(sort_order >= 0),
                file_name_snapshot TEXT NOT NULL,
                relative_path_snapshot TEXT NOT NULL,
                PRIMARY KEY(publication_id, sort_order),
                FOREIGN KEY(publication_id)
                    REFERENCES publications(id) ON DELETE CASCADE,
                FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE SET NULL
            ) WITHOUT ROWID;

            CREATE INDEX idx_publication_assets_asset
                ON publication_assets(asset_id, publication_id);
            """),
        new(
            8,
            "browse-sort-parity-indexes",
            """
            CREATE INDEX idx_assets_library_created_id
                ON assets(library_id, created_at_utc_ticks DESC, id DESC);

            CREATE INDEX idx_assets_library_size_id
                ON assets(library_id, file_size DESC, id DESC);

            CREATE INDEX idx_asset_user_metadata_rating_asset
                ON asset_user_metadata(rating DESC, asset_id DESC);

            CREATE INDEX idx_asset_user_metadata_status_asset
                ON asset_user_metadata(status_label COLLATE NOCASE, asset_id);
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

    public async Task CheckpointAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();
        command.CommandText =
            "PRAGMA wal_checkpoint(TRUNCATE);";

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "SQLite WAL checkpoint returned no status row.");
        }

        var busy = Convert.ToInt32(
            reader.GetValue(0),
            CultureInfo.InvariantCulture);

        if (busy != 0)
        {
            throw new InvalidOperationException(
                $"SQLite WAL checkpoint remained busy ({busy}).");
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
