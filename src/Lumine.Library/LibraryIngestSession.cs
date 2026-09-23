using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public sealed class LibraryIngestSession : IAsyncDisposable
{
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

    public long LibraryId { get; }

    public Task<int> WriteBatchAsync(
        IReadOnlyList<AssetUpsert> assets,
        CancellationToken cancellationToken = default)
    {
        var repository = _repository
            ?? throw new ObjectDisposedException(nameof(LibraryIngestSession));
        var connection = _connection
            ?? throw new ObjectDisposedException(nameof(LibraryIngestSession));

        return repository.UpsertAssetsOnConnectionAsync(
            connection,
            LibraryId,
            assets,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _repository = null;
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
