using System.Collections;

namespace Lumine.Viewer;

internal sealed class VirtualRowIndexList : IList
{
    private readonly int _rowCount;

    public VirtualRowIndexList(long itemCount, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);

        var rows = itemCount == 0
            ? 0
            : checked((itemCount + columns - 1) / columns);

        if (rows > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemCount),
                "Viewer row count exceeds Avalonia IList capacity.");
        }

        _rowCount = (int)rows;
    }

    public int Count => _rowCount;

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public object? this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_rowCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return (long)index;
        }
        set => throw new NotSupportedException();
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(object? value) =>
        value is long row && row >= 0 && row < _rowCount;

    public int IndexOf(object? value) =>
        value is long row && row >= 0 && row < _rowCount
            ? checked((int)row)
            : -1;

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var row = 0; row < _rowCount; row++)
        {
            array.SetValue((long)row, index + row);
        }
    }

    public IEnumerator GetEnumerator()
    {
        for (long row = 0; row < _rowCount; row++)
        {
            yield return row;
        }
    }
}
