using System.Collections;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>A list that is neither an array nor a <see cref="List{T}"/> and fails when anything asks for its enumerator, to prove a write never does.</summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class NoEnumeratorList<T>(params T[] elements) : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _inner = [.. elements];

    public int Count => _inner.Count;

    public bool IsReadOnly => false;

    public T this[int index]
    {
        get => _inner[index];
        set => _inner[index] = value;
    }

    public IEnumerator<T> GetEnumerator() => throw new NotSupportedException("A write must not enumerate.");

    IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException("A write must not enumerate.");

    public void Add(T item) => _inner.Add(item);

    public void Clear() => _inner.Clear();

    public bool Contains(T item) => _inner.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

    public int IndexOf(T item) => _inner.IndexOf(item);

    public void Insert(int index, T item) => _inner.Insert(index, item);

    public bool Remove(T item) => _inner.Remove(item);

    public void RemoveAt(int index) => _inner.RemoveAt(index);
}
