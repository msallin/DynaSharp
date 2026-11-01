using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>
/// One partition of a fan-out: its request, the one page it holds, and how far the caller got. Before it needs a page it owns no state of its own, so that a partition the
/// merge never reaches costs nothing.
/// </summary>
/// <typeparam name="T">How the items are read.</typeparam>
internal sealed class FanOutCursor<T>
    where T : IDynamoDbDocument<T>
{
    private readonly CommandState _template;
    private readonly AttributeValue _partitionKey;
    private readonly FanOutKeys _keys;
    private CommandState? _state;
    private int _version;
    private T[] _items = [];
    private int _next;
    private bool _hasBound;
    private bool _hasHint;

    /// <summary>Initializes a new instance of the <see cref="FanOutCursor{T}"/> class, at the start of its partition.</summary>
    /// <param name="template">The request every partition is a copy of.</param>
    /// <param name="plan">The plan of the read.</param>
    /// <param name="position">The position of the partition.</param>
    public FanOutCursor(CommandState template, FanOutPlan plan, int position)
    {
        _template = template;
        _partitionKey = plan.PartitionKeys[position];
        _keys = new FanOutKeys(plan, template.Pool);
        _keys.Start(default);
        Position = position;
    }

    /// <summary>Gets the position of the partition, which breaks ties.</summary>
    public int Position { get; }

    /// <summary>Gets the items DynamoDB read for this cursor so far, before filters.</summary>
    public int ScannedCount { get; private set; }

    /// <summary>Gets what DynamoDB charged this cursor so far, if the read asked for it.</summary>
    public ConsumedCapacity? ConsumedCapacity { get; private set; }

    /// <summary>Gets a value indicating whether an item is waiting.</summary>
    public bool HasItem => _next < _items.Length;

    /// <summary>Gets a value indicating whether nothing is left in the partition.</summary>
    public bool IsDone => !HasItem && !HasMore;

    /// <summary>Gets a value indicating whether nothing is known about what comes next: no item waits, and no key tells where the next one is.</summary>
    public bool IsUnknown => !HasItem && !_hasBound;

    /// <summary>
    /// Gets the sort key of what comes next. That is the item that waits, or else the key this cursor has read up to: everything still to come sorts after
    /// it, which is what lets the merge pass a partition by for as long as other items sort before that key.
    /// </summary>
    public ReadOnlySpan<byte> Head => _keys.Sort(HasItem ? _next + 1 : _keys.Count - 1);

    /// <summary>
    /// Gets the key a token continues after, which is empty at the start of the partition. It is the last item the caller got, not the end of the page, because what the
    /// page holds beyond that item is lost with this object and must be read again. If the page was given out whole, it is <c>LastEvaluatedKey</c>, which also skips what a filter dropped.
    /// </summary>
    public ReadOnlySpan<byte> ResumeKey
    {
        get
        {
            if (HasItem)
            {
                return _keys.Key(_next);
            }

            return _keys.Key(_hasHint ? 0 : _keys.Count - 1);
        }
    }

    /// <summary>
    /// Gets the key of the item that comes next, for a token, or nothing if that is not known. It saves the next page a read: what the start key alone says is that the
    /// partition goes on somewhere after the last item given, which puts it in the way of the merge. This says where it goes on, so the merge passes it by until that is its turn.
    /// </summary>
    public ReadOnlySpan<byte> HeadKey
    {
        get
        {
            if (HasItem)
            {
                return _keys.Key(_next + 1);
            }

            return _hasHint ? _keys.Key(_keys.Count - 1) : default;
        }
    }

    private bool HasMore { get; set; } = true;

    /// <summary>Starts from what a token says of a partition. The sort key of the hint, or else of the start key, says what the partition can still hold.</summary>
    /// <param name="start">The key to continue after as JSON, or nothing at the start of the partition.</param>
    /// <param name="head">The key of the item that comes next as JSON, or nothing if the token does not know it. It goes into no request.</param>
    /// <returns>The attribute that is wrong, or <see langword="null"/>.</returns>
    public string? StartAfter(ReadOnlySpan<byte> start, ReadOnlySpan<byte> head)
    {
        _keys.Start(start);
        _hasBound = true;
        _hasHint = !head.IsEmpty;
        var wrong = start.IsEmpty ? null : _keys.AddKey(start);
        return wrong ?? (_hasHint ? _keys.AddKey(head) : null);
    }

    /// <summary>Makes sure the cursor has a request, on the calling thread, so that cursors that read at once do not start from the template at once.</summary>
    public void Prepare()
    {
        if (_state is null)
        {
            var state = _template.StartPartition(_partitionKey, _keys);
            state.PageKey.Append(_keys.Key(0));
            _version = state.Version;
            _state = state;
        }
    }

    /// <summary>Replaces the page, which must be used up, with the next one.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that the caller awaits exactly once.</returns>
    public async ValueTask ReadPageAsync(CancellationToken cancellationToken)
    {
        var state = _state!;
        _keys.Start(state.PageKey.WrittenSpan);
        var page = await PagedRead<FanOutPageOperation<T>, T>.SendAsync(state, cancellationToken).ConfigureAwait(false);
        _items = page.Items;
        _next = 0;
        HasMore = page.HasMore;
        _hasBound = page.HasMore;
        _hasHint = false;
        ScannedCount += page.ScannedCount;
        ConsumedCapacity = CapacitySum.Add(ConsumedCapacity, state.ResponseCapacity);
        if (_hasBound && _keys.AddKey(state.PageKey.WrittenSpan) is { } attribute)
        {
            throw new DynamoDbMappingException(typeof(T).ToString(), attribute);
        }
    }

    /// <summary>Takes the item that waits.</summary>
    /// <returns>The item.</returns>
    public T Take() => _items[_next++];

    /// <summary>Gives back what the cursor owns. It can be called when there is nothing to give back.</summary>
    public void Release()
    {
        _state?.Release(_version);
        _state = null;
        _keys.Release();
    }
}
