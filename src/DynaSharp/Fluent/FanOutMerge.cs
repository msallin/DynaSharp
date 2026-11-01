using System.Runtime.CompilerServices;
using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>
/// The k-way merge of a fan-out: a binary heap of partitions, ordered by what comes next in each, with ties by position. Descending reverses the whole order.
/// </summary>
/// <remarks>
/// <para>
/// What is laziness elsewhere is the order here. A partition that holds no item still has a place in the heap, by the key it has read up to, because everything it can still give
/// sorts after that key. So when such a partition is on top, its next page is what the merge needs, and while it is not, nothing can make it necessary, however long its
/// turn takes. A partition that nothing is known about sorts first of all. There is no reading ahead, so between two requests no task exists: a <c>break</c>
/// has nothing to stop.
/// </para>
/// <para>
/// One page is what the merge needs at a time, unless it is at the start or a partition is on top that cannot be passed by. Then every partition that sorts before
/// the first item that waits has to be read before that item can go out, whatever they turn out to hold, so they are read at once, up to the limit of
/// concurrency.
/// </para>
/// </remarks>
/// <typeparam name="T">How the items are read.</typeparam>
internal sealed class FanOutMerge<T> : IDisposable
    where T : IDynamoDbDocument<T>
{
    private readonly CommandState _template;
    private readonly FanOutPlan _plan;
    private readonly bool _descending;
    private readonly FanOutCursor<T>?[] _cursors;
    private readonly FanOutCursor<T>[] _heap;
    private readonly List<FanOutCursor<T>> _needed = [];
    private readonly Func<int, CancellationToken, ValueTask> _readNeeded;
    private int _live;

    /// <summary>Initializes a new instance of the <see cref="FanOutMerge{T}"/> class.</summary>
    /// <param name="template">The request every partition is a copy of, with all its options.</param>
    /// <param name="plan">The plan of the read.</param>
    public FanOutMerge(CommandState template, FanOutPlan plan)
    {
        _template = template;
        _plan = plan;
        _descending = template.Descending;
        _cursors = new FanOutCursor<T>?[plan.PartitionKeys.Length];
        _heap = new FanOutCursor<T>[plan.PartitionKeys.Length];
        _readNeeded = ReadNeeded;
    }

    /// <summary>Gets the number of items DynamoDB read for this merge so far, before filters.</summary>
    public int ScannedCount
    {
        get
        {
            var total = 0;
            foreach (var cursor in _cursors)
            {
                total += cursor?.ScannedCount ?? 0;
            }

            return total;
        }
    }

    /// <summary>Gets what DynamoDB charged this merge so far, if the read asked for it: all responses it took, also those whose items did not make it into the page.</summary>
    public ConsumedCapacity? ConsumedCapacity
    {
        get
        {
            ConsumedCapacity? total = null;
            foreach (var cursor in _cursors)
            {
                total = CapacitySum.Add(total, cursor?.ConsumedCapacity);
            }

            return total;
        }
    }

    /// <summary>Puts every partition in its place. No request goes out.</summary>
    /// <param name="after">The position to continue from, or the default value for the start.</param>
    /// <exception cref="FormatException"><paramref name="after"/> is no position of this fan-out.</exception>
    public void Open(ContinuationToken after)
    {
        var entries = FanOutToken.Read(after, _cursors.Length, _descending);
        for (var position = 0; position < _cursors.Length; position++)
        {
            var entry = entries is null ? default : entries[position];
            if (entry.Type != AttributeType.Null)
            {
                var cursor = new FanOutCursor<T>(_template, _plan, position);
                _cursors[position] = cursor;
                _heap[_live++] = cursor;
                FanOutToken.Start(cursor, entry, _plan);
            }
        }

        Rebuild();
    }

    /// <summary>Moves on to the next item, reading what that takes.</summary>
    /// <param name="cancellationToken">Cancels the read, also between items of one page.</param>
    /// <returns><see langword="true"/> if there is an item for <see cref="Take"/>; a task that the caller awaits exactly once.</returns>
    public ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _live > 0 && _heap[0].HasItem ? new ValueTask<bool>(true) : ReadUntilItemAsync(cancellationToken);
    }

    /// <summary>Takes the next item, and with it the place of the partition in the order.</summary>
    /// <returns>The item.</returns>
    public T Take()
    {
        var top = _heap[0];
        var item = top.Take();
        if (top.IsDone)
        {
            top.Release();
            _heap[0] = _heap[--_live];
        }

        SiftDown(0);
        return item;
    }

    /// <summary>Writes the token of what is left.</summary>
    /// <returns>The token, or the default value if nothing is left.</returns>
    public ContinuationToken Next() => _live == 0 ? default : FanOutToken.Write(_cursors, _descending);

    /// <summary>Gives back what the partitions own. No request is in flight when a read ends.</summary>
    public void Dispose()
    {
        foreach (var cursor in _cursors)
        {
            cursor?.Release();
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> ReadUntilItemAsync(CancellationToken cancellationToken)
    {
        // Each round is a page: a page can be empty, which a filter does, and then the partition keeps its turn or passes it on.
        while (_live > 0 && !_heap[0].HasItem)
        {
            CollectNeeded();
            if (_needed.Count == 1)
            {
                await _needed[0].ReadPageAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await BoundedReads.RunAsync(_needed.Count, _plan.MaxConcurrency, _readNeeded, cancellationToken).ConfigureAwait(false);
            }

            Rebuild();
        }

        return _live > 0;
    }

    private ValueTask ReadNeeded(int index, CancellationToken cancellationToken) => _needed[index].ReadPageAsync(cancellationToken);

    private void CollectNeeded()
    {
        var top = _heap[0];
        FanOutCursor<T>? firstItem = null;
        for (var i = 0; i < _live; i++)
        {
            var cursor = _heap[i];
            if (cursor.HasItem && (firstItem is null || Compare(cursor, firstItem) < 0))
            {
                firstItem = cursor;
            }
        }

        _needed.Clear();
        for (var i = 0; i < _live; i++)
        {
            var cursor = _heap[i];
            if (!cursor.HasItem && (cursor.IsUnknown || cursor == top || (firstItem is not null && Compare(cursor, firstItem) < 0)))
            {
                cursor.Prepare();
                _needed.Add(cursor);
            }
        }
    }

    // Every cursor that was read has a new place, and the reads are much slower than this.
    private void Rebuild()
    {
        var live = 0;
        for (var i = 0; i < _live; i++)
        {
            var cursor = _heap[i];
            if (cursor.IsDone)
            {
                cursor.Release();
            }
            else
            {
                _heap[live++] = cursor;
            }
        }

        _live = live;
        for (var i = (live / 2) - 1; i >= 0; i--)
        {
            SiftDown(i);
        }
    }

    private void SiftDown(int index)
    {
        var heap = _heap;
        var live = _live;
        while (true)
        {
            var smallest = index;
            var left = (2 * index) + 1;
            var right = left + 1;
            if (left < live && Compare(heap[left], heap[smallest]) < 0)
            {
                smallest = left;
            }

            if (right < live && Compare(heap[right], heap[smallest]) < 0)
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

    private int Compare(FanOutCursor<T> a, FanOutCursor<T> b)
    {
        // A partition nothing is known about comes first in either direction: it could hold what comes first.
        if (a.IsUnknown != b.IsUnknown)
        {
            return a.IsUnknown ? -1 : 1;
        }

        var order = a.Head.SequenceCompareTo(b.Head);
        order = order != 0 ? order : a.Position.CompareTo(b.Position);
        return _descending ? -order : order;
    }
}
