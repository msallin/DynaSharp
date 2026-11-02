namespace DynaSharp.Fluent;

/// <summary>
/// A read as an <see cref="IAsyncEnumerable{T}"/>: it can be enumerated once, because a command is sent once, and the end of the enumeration gives the state
/// back, however it comes about: all pages read, a failure, a cancellation, a <c>break</c>, or an enumerator that was disposed before its first
/// <see cref="MoveNextAsync"/>. That last one is why this exists around the iterator, whose <c>finally</c> never runs if it never started. A read
/// that is never enumerated is abandoned, and its state collected, as for any command.
/// </summary>
/// <typeparam name="T">What is enumerated.</typeparam>
internal sealed class OnceEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
    private readonly CommandState _state;
    private readonly int _version;
    private readonly string _operation;
    private readonly TableModel _table;
    private readonly IAsyncEnumerable<T> _elements;
    private IAsyncEnumerator<T>? _enumerator;
    private int _enumerated;
    private bool _released;

    /// <summary>Initializes a new instance of the <see cref="OnceEnumerable{T}"/> class.</summary>
    /// <param name="state">The state of the read, which the caller entered.</param>
    /// <param name="operation">The operation, for the message.</param>
    /// <param name="elements">The iterator of the read, which does not release the state.</param>
    public OnceEnumerable(CommandState state, string operation, IAsyncEnumerable<T> elements)
    {
        _state = state;
        _version = state.Version;
        _operation = operation;
        _table = state.Table;
        _elements = elements;
    }

    /// <inheritdoc/>
    public T Current => _enumerator!.Current;

    /// <inheritdoc/>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) != 0)
        {
            throw new InvalidOperationException($"This {_operation} on {_table.Name} was enumerated already. A read is sent once; to read again, build the command again.");
        }

        _enumerator = _elements.GetAsyncEnumerator(cancellationToken);
        return this;
    }

    /// <inheritdoc/>
    public ValueTask<bool> MoveNextAsync() => _enumerator!.MoveNextAsync();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // The iterator first: it refuses while a page is in flight, and then the state must stay. A parallel scan waits for its segments in there.
        await _enumerator!.DisposeAsync().ConfigureAwait(false);
        if (!_released)
        {
            _released = true;
            _state.Release(_version);
        }
    }
}
