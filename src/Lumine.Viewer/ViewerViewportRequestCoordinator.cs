namespace Lumine.Viewer;

// Small, single-owner UI-thread state machine for per-viewport I/O
// revisions. It cannot create requests itself; an eventual adapter may
// use a lease.Token for a single bounded thumbnail batch. Cancellation
// of a previous lease is immediate upon direction/geometry change.
internal readonly record struct ViewerViewportRequestLease(
    long Revision,
    ViewerViewportRequestPlan Plan,
    CancellationToken Token,
    bool Changed);

internal sealed class ViewerViewportRequestCoordinator : IDisposable
{
    private CancellationTokenSource? _currentCancellation;
    private ViewerViewportRequestPlan _currentPlan;
    private long _revision;
    private bool _hasPlan;
    private bool _disposed;

    internal long Revision => _revision;

    internal ViewerViewportRequestLease Update(
        ViewerViewportRequestPlan next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hasPlan && next == _currentPlan)
        {
            return new ViewerViewportRequestLease(
                _revision,
                next,
                _currentCancellation?.Token
                    ?? new CancellationToken(canceled: true),
                Changed: false);
        }

        // One direction switch / fast seek cancels the entire previous
        // speculative range, rather than appending work per row attach.
        _currentCancellation?.Cancel();
        _currentCancellation?.Dispose();
        _currentCancellation = null;

        _hasPlan = true;
        _currentPlan = next;
        _revision++;

        if (!next.IsEmpty)
        {
            _currentCancellation = new CancellationTokenSource();
        }

        return new ViewerViewportRequestLease(
            _revision,
            next,
            _currentCancellation?.Token
                ?? new CancellationToken(canceled: true),
            Changed: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _currentCancellation?.Cancel();
        _currentCancellation?.Dispose();
        _currentCancellation = null;
    }
}
