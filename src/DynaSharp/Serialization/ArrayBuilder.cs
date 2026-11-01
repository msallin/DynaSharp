using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>
/// Collects elements of unknown count in a pooled array or a buffer of the caller, and produces an exactly sized array, so reading a list
/// or set allocates only the result.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>Use with <c>try</c>/<c>finally</c>, not <c>using</c>: a <c>using</c> variable is read-only, so <see cref="Add"/> would modify a copy.</remarks>
internal ref struct ArrayBuilder<T>
{
    private const int InitialCapacity = 16;

    private readonly ArrayPool<T> _pool;
    private Span<T> _items;
    private T[]? _rented;

    public ArrayBuilder(ArrayPool<T> pool)
    {
        _pool = pool;
        _rented = pool.Rent(InitialCapacity);
        _items = _rented;
    }

    /// <summary>Initializes a new instance of the <see cref="ArrayBuilder{T}"/> struct that rents only when the elements outgrow a buffer.</summary>
    /// <param name="pool">The pool to continue in.</param>
    /// <param name="buffer">The buffer to start in, typically on the stack. It needs no clearing afterwards.</param>
    public ArrayBuilder(ArrayPool<T> pool, Span<T> buffer)
    {
        _pool = pool;
        _items = buffer;
    }

    public int Count { readonly get; private set; }

    public readonly ReadOnlySpan<T> WrittenSpan => _items[..Count];

    public void Add(T item)
    {
        if (Count == _items.Length)
        {
            Grow();
        }

        _items[Count++] = item;
    }

    public readonly T[] ToArray() => _items[..Count].ToArray();

    public void Dispose()
    {
        ReturnRented();
        _items = default;
        Count = 0;
    }

    private void Grow()
    {
        var larger = _pool.Rent(_items.Length * 2);
        _items[..Count].CopyTo(larger);
        ReturnRented();
        _rented = larger;
        _items = larger;
    }

    // Only the used part holds references, so only that part is cleared before the array goes back to the pool. A disposed builder has
    // nothing rented, so disposing again returns nothing.
    private void ReturnRented()
    {
        if (_rented is not null)
        {
            _items[..Count].Clear();
            _pool.Return(_rented);
            _rented = null;
        }
    }
}
