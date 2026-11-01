namespace DynaSharp.Http;

/// <summary>
/// A small pool of reusable objects that holds at most as many as it was made for, and that can be closed. Taking and returning
/// an object costs one uncontended lock and no allocation. An object that does not fit, or that comes back after the pool is
/// closed, is the caller's to dispose.
/// </summary>
/// <remarks>
/// Not <c>ConcurrentQueue</c>: a queue allocates a segment for every few dozen objects that pass through it, and has no bound.
/// What a caller does between <see cref="Rent"/> and <see cref="Return"/> is its own business; the pool only guarantees that
/// an object is in the pool or with one caller, never both, as long as every caller returns an object once at most.
/// </remarks>
/// <typeparam name="T">The pooled type.</typeparam>
internal sealed class BoundedPool<T>(int capacity)
    where T : class
{
    private readonly Lock _gate = new();
    private readonly T?[] _items = new T?[capacity];
    private int _count;
    private bool _closed;

    /// <summary>Takes an object out of the pool.</summary>
    /// <returns>The object, or <see langword="null"/> if the pool is empty.</returns>
    public T? Rent()
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return null;
            }

            var item = _items[--_count];
            _items[_count] = null;
            return item;
        }
    }

    /// <summary>Puts an object back.</summary>
    /// <param name="item">The object, which the caller no longer uses.</param>
    /// <returns><see langword="false"/> if the pool is full or closed, in which case the caller disposes the object.</returns>
    public bool Return(T item)
    {
        lock (_gate)
        {
            if (_closed || _count == _items.Length)
            {
                return false;
            }

            _items[_count++] = item;
            return true;
        }
    }

    /// <summary>Closes the pool, so that it takes nothing more, and hands out what it holds.</summary>
    /// <returns>The objects that were in the pool, for the caller to dispose.</returns>
    public T[] Close()
    {
        lock (_gate)
        {
            _closed = true;
            var held = new T[_count];
            Array.Copy(_items, held, _count);
            Array.Clear(_items, 0, _count);
            _count = 0;
            return held;
        }
    }
}
