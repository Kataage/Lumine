using System.Diagnostics;
using Lumine.Core;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

/// <summary>
/// A source-version-guarded image technical metadata update. A rejected row never
/// receives an UPSERT; other valid rows in the same bounded transaction may commit.
/// </summary>
public sealed record TechnicalMetadataBatchItem(
    long AssetId,
    long ExpectedSourceRevision,
    long ExpectedFileSize,
    long ExpectedModifiedAtUtcTicks,
    AssetTechnicalMetadata Metadata);

/// <summary>
/// Source-guarded batch results and the time spent inside the successful
/// SQLite COMMIT only. This excludes connection setup, SQL work and queue wait.
/// </summary>
public readonly record struct TechnicalMetadataBatchCommitResult(
    IReadOnlyList<bool> Results,
    long CommitTicks);

public sealed partial class LibraryRepository
{
    public const int MaxTechnicalMetadataBatchSize = 64;

    /// <summary>
    /// Persist up to 64 technical metadata results with one SQLite transaction.
    /// Every row uses the exact same revision, stat and normalized source-identity
    /// guard as UpdateTechnicalMetadataAsync. A false result means a stale or
    /// conflicting source and does not alter that row. Failure/cancellation rolls
    /// back the entire batch, and no success is returned before COMMIT.
    /// </summary>
    public async Task<IReadOnlyList<bool>> UpdateTechnicalMetadataBatchAsync(
        long libraryId,
        IReadOnlyList<TechnicalMetadataBatchItem> items,
        CancellationToken cancellationToken = default) =>
        (await UpdateTechnicalMetadataBatchCoreAsync(
            libraryId, items, measureCommit: false, cancellationToken)
            .ConfigureAwait(false)).Results;

    /// <summary>
    /// Same committed and source-guarded production batch path, but expose
    /// the actual COMMIT interval for bounded Viewer diagnostics. No per-row
    /// clocks, extra SQL or measurement work on other batch callers.
    /// </summary>
    public Task<TechnicalMetadataBatchCommitResult>
        UpdateTechnicalMetadataBatchMeasuredAsync(
            long libraryId,
            IReadOnlyList<TechnicalMetadataBatchItem> items,
            CancellationToken cancellationToken = default) =>
        UpdateTechnicalMetadataBatchCoreAsync(
            libraryId, items, measureCommit: true, cancellationToken);

    private async Task<TechnicalMetadataBatchCommitResult>
        UpdateTechnicalMetadataBatchCoreAsync(
            long libraryId,
            IReadOnlyList<TechnicalMetadataBatchItem> items,
            bool measureCommit,
            CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(libraryId);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > MaxTechnicalMetadataBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(items),
                $"Technical metadata batch must contain at most {MaxTechnicalMetadataBatchSize} items.");
        }

        if (items.Count == 0)
        {
            return new TechnicalMetadataBatchCommitResult(Array.Empty<bool>(), 0L);
        }

        // Validate and snapshot before opening a transaction. Caller mutation
        // cannot change source guards while the batch is being committed.
        var prepared = new (TechnicalMetadataBatchItem Item, string Identity)[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index] ?? throw new ArgumentException(
                "Technical metadata batches cannot contain null entries.", nameof(items));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(item.AssetId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(item.ExpectedSourceRevision);
            ArgumentOutOfRangeException.ThrowIfNegative(item.ExpectedFileSize);
            var metadata = item.Metadata ?? throw new ArgumentException(
                "Technical metadata is required.", nameof(items));
            if (metadata.Width <= 0
                || metadata.Height <= 0
                || metadata.RawWidth <= 0
                || metadata.RawHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items), "Technical image dimensions must be positive.");
            }

            if (string.IsNullOrWhiteSpace(metadata.Format) || metadata.Format.Length > 64)
            {
                throw new ArgumentException(
                    "Technical image format must be present and at most 64 characters.",
                    nameof(items));
            }

            if (!FileSourceIdentityProbe.IsValid(metadata.SourceIdentity))
            {
                throw new ArgumentException(
                    "Source identity must be a valid NTFS-USN or SHA-256 identity.",
                    nameof(items));
            }

            prepared[index] = (item with { Metadata = metadata with { } },
                FileSourceIdentityProbe.Normalize(metadata.SourceIdentity));
        }

        var nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = await _database.OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            try
            {
                using var transaction = connection.BeginTransaction();
                var result = new bool[prepared.Length];

                for (var index = 0; index < prepared.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (item, normalizedIdentity) = prepared[index];
                    result[index] = await UpdateTechnicalMetadataOnConnectionAsync(
                        connection,
                        libraryId,
                        item.AssetId,
                        item.ExpectedSourceRevision,
                        item.ExpectedFileSize,
                        item.ExpectedModifiedAtUtcTicks,
                        item.Metadata,
                        normalizedIdentity,
                        nowTicks,
                        cancellationToken,
                        timingObserver: null,
                        connectionOpenTicks: 0L,
                        batchTransaction: transaction).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var commitStarted = measureCommit ? Stopwatch.GetTimestamp() : 0L;
                transaction.Commit();
                var commitTicks = measureCommit
                    ? Stopwatch.GetElapsedTime(commitStarted).Ticks
                    : 0L;
                return new TechnicalMetadataBatchCommitResult(result, commitTicks);
            }
            catch (SqliteException exception)
                when (attempt == 0 && IsRecoverableNestedTransactionState(exception))
            {
                // The underlying SQLite pooled connection may retain a poisoned
                // transaction state. Preserve the established one-retry quarantine.
                SqliteConnection.ClearPool(connection);
            }
        }

        throw new InvalidOperationException(
            "Technical metadata batch retry loop completed unexpectedly.");
    }
}
