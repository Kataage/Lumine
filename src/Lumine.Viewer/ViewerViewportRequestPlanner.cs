namespace Lumine.Viewer;

// This is a deliberately UI-free request planning primitive, NOT yet wired
// to the gallery. Native Avalonia virtualization decides which controls exist;
// viewport geometry alone determines the bounded set of assets for which
// thumbnail I/O is permitted. This prevents visual-tree overscan/fast-seek
// churn from silently growing background source request counts.
internal readonly record struct ViewerViewportRequestPlan(
    long VisibleStartIndex,
    int VisibleCount,
    long ImminentStartIndex,
    int ImminentCount,
    int ScrollDirection)
{
    internal static ViewerViewportRequestPlan Empty =>
        new(0, 0, 0, 0, 1);

    internal bool IsEmpty => VisibleCount == 0;

    internal long VisibleEndExclusive =>
        VisibleStartIndex + VisibleCount;

    internal long ImminentEndExclusive =>
        ImminentStartIndex + ImminentCount;
}

internal static class ViewerViewportRequestPlanner
{
    /// <summary>
    /// Calculate at most one row of directional imminent I/O from *visible*
    /// row geometry. No attached-control count, CacheLength, or virtualized
    /// overscan range can change this result.
    ///
    /// Return an empty plan when layout geometry is temporarily invalid
    /// (during a fast seek/recycle), instead of firing speculative requests
    /// against an obsolete row.
    /// </summary>
    internal static ViewerViewportRequestPlan Build(
        long assetCount,
        int columns,
        long firstVisibleRow,
        long lastVisibleRow,
        int scrollDirection,
        int maxImminentAssets)
    {
        if (assetCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(assetCount));
        }

        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (scrollDirection is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(scrollDirection));
        }

        if (maxImminentAssets < 0 || maxImminentAssets > columns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxImminentAssets));
        }

        if (assetCount == 0
            || firstVisibleRow < 0
            || lastVisibleRow < firstVisibleRow)
        {
            return ViewerViewportRequestPlan.Empty;
        }

        // No (assetCount + columns - 1): that expression can overflow
        // for assetCount=long.MaxValue.
        var rowCount = assetCount / columns
            + (assetCount % columns > 0 ? 1 : 0);
        if (lastVisibleRow >= rowCount)
        {
            return ViewerViewportRequestPlan.Empty;
        }

        var start = checked(firstVisibleRow * columns);
        var lastStart = checked(lastVisibleRow * columns);
        var end = lastStart
            + Math.Min((long)columns, assetCount - lastStart);
        var visibleCount = end - start;
        if (visibleCount > int.MaxValue)
        {
            // A real viewport must have a bounded number of realized
            // items. Failing closed prevents huge unbounded requests.
            return ViewerViewportRequestPlan.Empty;
        }

        var nextRow = scrollDirection > 0
            ? lastVisibleRow + 1
            : firstVisibleRow - 1;
        if (nextRow < 0 || nextRow >= rowCount
            || maxImminentAssets == 0)
        {
            return new ViewerViewportRequestPlan(
                start, (int)visibleCount, 0, 0, scrollDirection);
        }

        var nextStart = checked(nextRow * columns);
        var nextCount = checked((int)Math.Min(
            maxImminentAssets,
            assetCount - nextStart));
        return new ViewerViewportRequestPlan(
            start, (int)visibleCount,
            nextStart, nextCount,
            scrollDirection);
    }
}
