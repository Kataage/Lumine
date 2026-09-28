using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed partial class LumineV1Importer
{
    private static async Task<SqliteConnection> OpenSnapshotAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        };

        var connection =
            new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command =
            connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA query_only=ON;
            PRAGMA busy_timeout=5000;
            """;
        await command.ExecuteNonQueryAsync(
            cancellationToken).ConfigureAwait(false);

        return connection;
    }

    private static async Task<bool> HasTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = $name
            );
            """;
        command.Parameters.AddWithValue(
            "$name",
            tableName);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<HashSet<string>>
        GetTableColumnsAsync(
            SqliteConnection connection,
            string tableName,
            CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(
            StringComparer.Ordinal);
        var quoted = QuoteIdentifier(tableName);

        await using var command =
            connection.CreateCommand();
        command.CommandText =
            $"PRAGMA table_info({quoted});";

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            result.Add(reader.GetString(1));
        }

        return result;
    }

    private static async Task<long> CountRowsAsync(
        SqliteConnection connection,
        string tableName,
        string? where,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}"
            + (string.IsNullOrWhiteSpace(where)
                ? ";"
                : $" WHERE {where};");

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(
                cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static string QuoteIdentifier(
        string value) =>
        """ + value.Replace(
            """,
            """",
            StringComparison.Ordinal) + """;

    private static int ParseMigrationVersion(
        string name)
    {
        var separator = name.IndexOf('_');
        var prefix = separator < 0
            ? name
            : name[..separator];

        return int.TryParse(
            prefix,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var version)
            ? version
            : 0;
    }

    private static string? NormalizeOptionalLabel(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Trim()
            .Normalize(NormalizationForm.FormKC);
    }

    private static NormalizedTag NormalizeTag(
        string value)
    {
        var name = value
            .Trim()
            .Normalize(NormalizationForm.FormKC);
        return new NormalizedTag(
            name,
            name.ToUpperInvariant());
    }

    private static string? ReadNullableText(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToString(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);

    private static long ParseLegacyTimestamp(
        string? primary,
        string? fallback,
        out bool usedFallback)
    {
        if (TryParseLegacyTimestamp(
                primary,
                out var primaryTicks))
        {
            usedFallback = false;
            return primaryTicks;
        }

        if (TryParseLegacyTimestamp(
                fallback,
                out var fallbackTicks))
        {
            usedFallback = true;
            return fallbackTicks;
        }

        usedFallback = true;
        return DateTimeOffset.UnixEpoch.UtcDateTime.Ticks;
    }

    private static bool TryParseLegacyTimestamp(
        string? value,
        out long ticks)
    {
        ticks = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        ticks = parsed.UtcDateTime.Ticks;
        return true;
    }

    private static LumineV1ImportDiagnostic Error(
        string code,
        string message) =>
        new(
            LumineV1ImportDiagnosticSeverity.Error,
            code,
            message);

    private static LumineV1ImportDiagnostic Warning(
        string code,
        string message) =>
        new(
            LumineV1ImportDiagnosticSeverity.Warning,
            code,
            message);

    private const string LegacyAssetSelectSql =
        """
        SELECT
            a.id,
            a.library_id,
            a.file_path,
            a.file_size,
            a.modified_at_fs,
            a.width,
            a.height,
            a.rating,
            a.status_label,
            a.is_favorite,
            a.color_label,
            a.updated_at,
            n.content
        FROM assets AS a
        LEFT JOIN asset_notes AS n
            ON n.asset_id = a.id
        ORDER BY a.library_id ASC, a.id ASC;
        """;

    private sealed record LegacyLibrary(
        long Id,
        string Name,
        string RootPath,
        bool IsEnabled);

    private sealed record LegacyAsset(
        long Id,
        long LibraryId,
        string FilePath,
        long FileSize,
        long ModifiedAtUtcTicks,
        int Width,
        int Height,
        int Rating,
        string StatusLabel,
        bool Favorite,
        string? ColorLabel,
        string Notes,
        bool UsedTimestampFallback);

    private readonly record struct TagSummary(
        int Count,
        int DistinctCount,
        bool HasColor,
        bool Invalid);

    private readonly record struct DestinationLibrary(
        long Id,
        string RootPath);

    private readonly record struct DestinationAsset(
        long AssetId,
        long LibraryId,
        bool AllowTagImport);

    private readonly record struct NormalizedTag(
        string Name,
        string Key);

    private sealed class SourceSnapshot : IDisposable
    {
        private SourceSnapshot(
            string sourceDatabasePath,
            string rootPath,
            string databasePath,
            string fingerprintSha256,
            long sourceBytes)
        {
            SourceDatabasePath = sourceDatabasePath;
            RootPath = rootPath;
            DatabasePath = databasePath;
            FingerprintSha256 =
                fingerprintSha256;
            SourceBytes = sourceBytes;
        }

        public string SourceDatabasePath { get; }

        public string RootPath { get; }

        public string DatabasePath { get; }

        public string FingerprintSha256 { get; }

        public long SourceBytes { get; }

        public static async Task<SourceSnapshot> CreateAsync(
            string sourceDatabasePath,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                sourceDatabasePath);

            var sourcePath =
                Path.GetFullPath(sourceDatabasePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "Lumine v1 database was not found.",
                    sourcePath);
            }

            var before = await CaptureFingerprintAsync(
                sourcePath,
                cancellationToken).ConfigureAwait(false);

            var root = Path.Combine(
                Path.GetTempPath(),
                $"lumine-v1-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            try
            {
                var snapshotPath = Path.Combine(
                    root,
                    Path.GetFileName(sourcePath));

                await CopyFileAsync(
                    sourcePath,
                    snapshotPath,
                    cancellationToken).ConfigureAwait(false);

                var sourceWal = sourcePath + "-wal";
                if (File.Exists(sourceWal))
                {
                    await CopyFileAsync(
                        sourceWal,
                        snapshotPath + "-wal",
                        cancellationToken).ConfigureAwait(false);
                }

                var after = await CaptureFingerprintAsync(
                    sourcePath,
                    cancellationToken).ConfigureAwait(false);

                if (!string.Equals(
                        before.FingerprintSha256,
                        after.FingerprintSha256,
                        StringComparison.Ordinal)
                    || before.TotalBytes != after.TotalBytes)
                {
                    throw new LumineV1SourceChangedException(
                        "The Lumine v1 database changed while its read-only migration snapshot was being created. Close Lumine v1 and retry.");
                }

                return new SourceSnapshot(
                    sourcePath,
                    root,
                    snapshotPath,
                    before.FingerprintSha256,
                    before.TotalBytes);
            }
            catch
            {
                DeleteDirectoryBestEffort(root);
                throw;
            }
        }

        public void Dispose() =>
            DeleteDirectoryBestEffort(RootPath);

        private static async Task<SourceFingerprint>
            CaptureFingerprintAsync(
                string databasePath,
                CancellationToken cancellationToken)
        {
            using var hash =
                IncrementalHash.CreateHash(
                    HashAlgorithmName.SHA256);
            long totalBytes = 0;

            foreach (var (path, label) in new[]
                     {
                         (databasePath, "database"),
                         (databasePath + "-wal", "wal")
                     })
            {
                if (!File.Exists(path))
                {
                    hash.AppendData(
                        Encoding.UTF8.GetBytes(
                            label + ":absent\n"));
                    continue;
                }

                var info = new FileInfo(path);
                info.Refresh();
                totalBytes = checked(
                    totalBytes + info.Length);

                hash.AppendData(
                    Encoding.UTF8.GetBytes(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{label}:{info.Length}\n")));

                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous
                        | FileOptions.SequentialScan);

                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await stream.ReadAsync(
                        buffer,
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    hash.AppendData(
                        buffer.AsSpan(0, read));
                }
            }

            return new SourceFingerprint(
                Convert.ToHexString(
                        hash.GetHashAndReset())
                    .ToLowerInvariant(),
                totalBytes);
        }

        private static async Task CopyFileAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous
                    | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous
                    | FileOptions.SequentialScan);

            await source.CopyToAsync(
                destination,
                64 * 1024,
                cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(
                cancellationToken).ConfigureAwait(false);
        }

        private static void DeleteDirectoryBestEffort(
            string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(
                        path,
                        recursive: true);
                }
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException)
            {
            }
        }
    }

    private readonly record struct SourceFingerprint(
        string FingerprintSha256,
        long TotalBytes);
}
