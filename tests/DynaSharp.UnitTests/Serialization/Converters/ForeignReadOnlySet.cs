using System.Collections;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>A set that is not a <see cref="HashSet{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ForeignReadOnlySet<T>(params T[] elements) : IReadOnlySet<T>
{
    private readonly HashSet<T> _inner = [.. elements];

    public int Count => _inner.Count;

    public bool Contains(T item) => _inner.Contains(item);

    public bool IsProperSubsetOf(IEnumerable<T> other) => _inner.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => _inner.IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<T> other) => _inner.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => _inner.IsSupersetOf(other);

    public bool Overlaps(IEnumerable<T> other) => _inner.Overlaps(other);

    public bool SetEquals(IEnumerable<T> other) => _inner.SetEquals(other);

    public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
