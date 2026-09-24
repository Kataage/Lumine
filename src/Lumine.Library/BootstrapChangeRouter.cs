namespace Lumine.Library;

internal sealed class BootstrapChangeRouter
{
    private const int Capacity = 4096;

    private readonly object _gate = new();
    private readonly List<DirectoryChange> _buffer = new(Capacity);
    private LibraryChangeProcessor? _live;
    private bool _overflowed;

    public void Publish(IReadOnlyList<DirectoryChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        LibraryChangeProcessor? live;

        lock (_gate)
        {
            live = _live;
            if (live is null)
            {
                foreach (var change in changes)
                {
                    if (change.Kind == DirectoryChangeKind.Overflow)
                    {
                        _overflowed = true;
                        continue;
                    }

                    if (_buffer.Count >= Capacity)
                    {
                        _overflowed = true;
                        continue;
                    }

                    _buffer.Add(change);
                }

                return;
            }
        }

        live.Publish(changes);
    }

    public bool Activate(LibraryChangeProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);

        lock (_gate)
        {
            if (_live is not null)
            {
                throw new InvalidOperationException(
                    "Bootstrap change router is already active.");
            }

            if (_buffer.Count > 0)
            {
                processor.Publish(_buffer);
                _buffer.Clear();
            }

            if (_overflowed)
            {
                processor.Publish(
                    [
                        new DirectoryChange(
                            DirectoryChangeKind.Overflow,
                            string.Empty,
                            null,
                            DateTimeOffset.UtcNow)
                    ]);
            }

            _live = processor;
            return _overflowed;
        }
    }
}
