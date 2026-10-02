using System.Collections;

namespace Lumine.Viewer;

/// <summary>
/// Compact selection model for very large Viewer result sets.
/// Stores non-overlapping inclusive ranges so Select-All and Shift ranges
/// remain O(1) in the common case instead of materializing one entry per asset.
/// </summary>
internal sealed class ViewerRangeSelection
{
    private readonly List<SelectionRange> _ranges = [];

    public int Count
    {
        get
        {
            long count = 0;
            foreach (var range in _ranges)
            {
                count += range.Count;
            }

            return checked((int)count);
        }
    }

    public bool IsEmpty => _ranges.Count == 0;

    internal int RangeCount =>
        _ranges.Count;

    public long Min =>
        _ranges.Count == 0
            ? throw new InvalidOperationException("Selection is empty.")
            : _ranges[0].Start;

    public long Max =>
        _ranges.Count == 0
            ? throw new InvalidOperationException("Selection is empty.")
            : _ranges[^1].End;

    public bool Contains(long value)
    {
        foreach (var range in _ranges)
        {
            if (value < range.Start)
            {
                return false;
            }

            if (value <= range.End)
            {
                return true;
            }
        }

        return false;
    }

    public bool SetSingle(long value) =>
        SetRange(value, value);

    public bool SetRange(long start, long end)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (_ranges.Count == 1
            && _ranges[0].Start == start
            && _ranges[0].End == end)
        {
            return false;
        }

        _ranges.Clear();
        _ranges.Add(new SelectionRange(start, end));
        return true;
    }

    public bool SelectAll(long assetCount)
    {
        if (assetCount <= 0)
        {
            return Clear();
        }

        return SetRange(0, assetCount - 1);
    }

    /// <summary>
    /// Toggles one value and returns whether the value is selected afterwards.
    /// </summary>
    public bool Toggle(long value)
    {
        for (var index = 0; index < _ranges.Count; index++)
        {
            var range = _ranges[index];

            if (value < range.Start)
            {
                InsertAndMerge(index, value);
                return true;
            }

            if (value > range.End)
            {
                continue;
            }

            if (range.Start == range.End)
            {
                _ranges.RemoveAt(index);
            }
            else if (value == range.Start)
            {
                _ranges[index] =
                    range with
                    {
                        Start = range.Start + 1
                    };
            }
            else if (value == range.End)
            {
                _ranges[index] =
                    range with
                    {
                        End = range.End - 1
                    };
            }
            else
            {
                _ranges[index] =
                    range with
                    {
                        End = value - 1
                    };
                _ranges.Insert(
                    index + 1,
                    new SelectionRange(
                        value + 1,
                        range.End));
            }

            return false;
        }

        InsertAndMerge(_ranges.Count, value);
        return true;
    }

    public bool Clear()
    {
        if (_ranges.Count == 0)
        {
            return false;
        }

        _ranges.Clear();
        return true;
    }

    public IReadOnlyList<long> AsReadOnlyList() =>
        new SelectionIndexList(
            [.. _ranges],
            Count);

    private void InsertAndMerge(
        int index,
        long value)
    {
        var mergePrevious =
            index > 0
            && _ranges[index - 1].End + 1 == value;
        var mergeNext =
            index < _ranges.Count
            && _ranges[index].Start - 1 == value;

        if (mergePrevious && mergeNext)
        {
            var previous = _ranges[index - 1];
            var next = _ranges[index];
            _ranges[index - 1] =
                new SelectionRange(
                    previous.Start,
                    next.End);
            _ranges.RemoveAt(index);
            return;
        }

        if (mergePrevious)
        {
            var previous = _ranges[index - 1];
            _ranges[index - 1] =
                previous with
                {
                    End = value
                };
            return;
        }

        if (mergeNext)
        {
            var next = _ranges[index];
            _ranges[index] =
                next with
                {
                    Start = value
                };
            return;
        }

        _ranges.Insert(
            index,
            new SelectionRange(value, value));
    }

    private readonly record struct SelectionRange(
        long Start,
        long End)
    {
        public long Count =>
            checked(End - Start + 1);
    }

    private sealed class SelectionIndexList
        : IReadOnlyList<long>
    {
        private readonly SelectionRange[] _ranges;

        public SelectionIndexList(
            SelectionRange[] ranges,
            int count)
        {
            _ranges = ranges;
            Count = count;
        }

        public int Count { get; }

        public long this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                if (index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                long offset = index;
                foreach (var range in _ranges)
                {
                    if (offset < range.Count)
                    {
                        return range.Start + offset;
                    }

                    offset -= range.Count;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public IEnumerator<long> GetEnumerator()
        {
            foreach (var range in _ranges)
            {
                for (var value = range.Start;
                     value <= range.End;
                     value++)
                {
                    yield return value;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
