using System.Collections;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>A read-only dictionary that is not a <see cref="Dictionary{TKey, TValue}"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
internal sealed class ForeignReadOnlyDictionary<T>(Dictionary<string, T> entries) : IReadOnlyDictionary<string, T>
{
    public int Count => entries.Count;

    public IEnumerable<string> Keys => entries.Keys;

    public IEnumerable<T> Values => entries.Values;

    public T this[string key] => entries[key];

    public bool ContainsKey(string key) => entries.ContainsKey(key);

    public bool TryGetValue(string key, out T value) => entries.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, T>> GetEnumerator() => entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
