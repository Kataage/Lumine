using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed class LibraryIngestSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private LibraryRepository? _repository;
    private SqliteConnection? _connection;

    internal LibraryIngestSession(
        LibraryRepository repository,
        SqliteConnection connection,
        long libraryId)
    {
        _repository = repository;
        _connection = connection;
        LibraryId = libraryId;
    }

    internal static async Task<LibraryIngestSession> CreateAsync(
        LibraryRepository repository,
        SqliteConnection connection,
        long libraryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA wal_autocheckpoint=0;
            PRAGMA cache_size=-32768;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return new LibraryIngestSession(repository, connection, libraryId);
    }

    public long LibraryId { get; }

    public async Task<int> WriteBatchAsync(
        IReadOnlyList<AssetUpsert> assets,
        CancellationToken cancellationToken = default)
    {
        var repository = _repository
            ?? throw new ObjectDisposedException(nameof(LibraryIngestSession));
        var connection = _connection
            ?? throw new ObjectDisposedException(nameof(LibraryIngestSession));

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_repository is null || !ReferenceEquals(_connection, connection))
            {
                throw new ObjectDisposedException(nameof(LibraryIngestSession));
            }

            return await repository.UpsertAssetsOnConnectionAsync(
                connection,
                LibraryId,
                assets,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _repository = null;

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var connection = Interlocked.Exchange(ref _connection, null);
            if (connection is null)
            {
                return;
            }

            try
            {
                await using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                await checkpoint.ExecuteNonQueryAsync().ConfigureAwait(false);

                await using var reset = connection.CreateCommand();
                reset.CommandText =
                    """
                    PRAGMA wal_autocheckpoint=1000;
                    PRAGMA cache_size=-2000;
                    """;
                await reset.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
