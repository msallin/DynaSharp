using System.Collections;

namespace DynaSharp.Generators;

/// <summary>
/// An array that compares by its elements. A plain array or an <c>ImmutableArray&lt;T&gt;</c> in a pipeline value compares by reference,
/// which would silently defeat the incremental cache.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    /// <summary>Initializes a new instance of the <see cref="EquatableArray{T}"/> struct.</summary>
    /// <param name="items">The elements, which the caller no longer changes.</param>
    public EquatableArray(T[] items)
    {
        _items = items;
    }

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public bool Equals(EquatableArray<T> other) => new ReadOnlySpan<T>(_items).SequenceEqual(new ReadOnlySpan<T>(other._items));

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    // Hand-written FNV over the element hashes: HashCode does not exist on netstandard2.0.
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var item in new ReadOnlySpan<T>(_items))
            {
                hash = (hash ^ item.GetHashCode()) * 16777619;
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
