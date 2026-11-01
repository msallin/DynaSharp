using System.Text;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// The attribute names of an <see cref="Item"/>, with the index that lookups in large items use.
/// </summary>
/// <remarks>
/// <para>
/// A shared shape holds exactly the names of the items read with it, is never modified and may be used by any number of items and
/// threads, so a page of items that look alike holds its names once, and the index costs one dictionary per shape, not per item. The
/// shape defines the order of the values: items with the same names in another order use the same shape.
/// </para>
/// <para>A private shape belongs to one item built by hand and grows with it. Its array can be longer than the item has attributes.</para>
/// </remarks>
internal sealed class ItemShape
{
    // The lookup benchmark of the API layers proposal: 207 ns linear against 87 ns indexed at 8 attributes with same-prefix names. An index
    // is built once for all items of a shared shape, so it pays early; the index of a private shape serves one item, so it has to earn more.
    private const int SharedLinearLimit = 8;
    private const int PrivateLinearLimit = 32;

    // Up to this many names, comparing each with the ones before is cheaper than a hash set.
    private const int DuplicateScanLimit = 32;

    // What a name costs a table that retains the shape, besides its characters: string header, array slot and index entry.
    private const int NameOverheadBytes = 64;

    private Dictionary<string, int>? _index;

    private ItemShape(string[] names, bool isShared, int nameHash)
    {
        Names = names;
        IsShared = isShared;
        NameHash = nameHash;
    }

    /// <summary>Gets the shared shape of an item without attributes.</summary>
    public static ItemShape Empty { get; } = new([], isShared: true, nameHash: 0);

    public string[] Names { get; }

    public bool IsShared { get; }

    /// <summary>Gets the hash of the names of a shared shape, see <see cref="HashNames"/>.</summary>
    public int NameHash { get; }

    /// <summary>Gets what the shape keeps alive, in bytes: the bound on what a table that retains it can hold.</summary>
    public long RetainedBytes
    {
        get
        {
            var bytes = 0L;
            foreach (var name in Names)
            {
                bytes += (name.Length * sizeof(char)) + NameOverheadBytes;
            }

            return bytes;
        }
    }

    /// <summary>Gets a hash of the names that does not depend on their order: the hashes of the names are added up.</summary>
    /// <param name="names">The names.</param>
    /// <returns>The hash.</returns>
    public static int HashNames(ReadOnlySpan<string> names)
    {
        var hash = 0u;
        foreach (var name in names)
        {
            hash += NameTable.Hash(name);
        }

        return (int)hash;
    }

    /// <summary>Creates a shared shape from the names of a read item. This is the one place that checks them for duplicates.</summary>
    /// <param name="names">The names, copied.</param>
    /// <param name="nameHash">The result of <see cref="HashNames"/> for them.</param>
    /// <returns>The shape.</returns>
    /// <exception cref="JsonException">A name occurs twice.</exception>
    public static ItemShape CreateShared(ReadOnlySpan<string> names, int nameHash)
    {
        ThrowOnDuplicates(names);
        return new ItemShape(names.ToArray(), isShared: true, nameHash);
    }

    public static ItemShape CreatePrivate(int capacity) => new(new string[capacity], isShared: false, nameHash: 0);

    /// <summary>Compares a property name in the JSON with the name at a position, without allocating.</summary>
    /// <param name="json">The JSON reader, positioned on a property name.</param>
    /// <param name="position">The position in <see cref="Names"/>.</param>
    /// <returns><see langword="true"/> if both are the same text, in whatever spelling the JSON has it.</returns>
    public bool IsNameAt(ref Utf8JsonReader json, int position)
    {
        // The raw bytes of an escaped name say nothing: a\\b in JSON is three characters, and would match a stored name of four.
        var name = Names[position];
        return (!json.ValueIsEscaped && Ascii.Equals(json.ValueSpan, name)) || json.ValueTextEquals(name);
    }

    /// <summary>Finds the position of a name among the first <paramref name="count"/> names.</summary>
    /// <param name="name">The name, compared ordinally.</param>
    /// <param name="count">The number of attributes the item has.</param>
    /// <returns>The position, or -1.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public int Find(string name, int count)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (count > (IsShared ? SharedLinearLimit : PrivateLinearLimit))
        {
            return (_index ?? BuildIndex(count)).TryGetValue(name, out var position) ? position : -1;
        }

        var names = Names;
        for (var i = 0; i < count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Puts the values of an item into the order of this shape if the item has exactly these names, in any order. A name that is
    /// missing here, or that occurs twice in the item and so leaves another position without a value, means another shape.
    /// </summary>
    /// <param name="names">The names as read. A duplicate is possible.</param>
    /// <param name="values">The values as read.</param>
    /// <param name="ordered">The result, of the length of <paramref name="names"/> and all default; left that way on failure.</param>
    /// <returns><see langword="true"/> if the item has this shape.</returns>
    public bool TryOrder(ReadOnlySpan<string> names, ReadOnlySpan<AttributeValue> values, Span<AttributeValue> ordered)
    {
        if (names.Length != Names.Length)
        {
            return false;
        }

        if (names.SequenceEqual(Names))
        {
            values.CopyTo(ordered);
            return true;
        }

        for (var i = 0; i < names.Length; i++)
        {
            var position = Find(names[i], Names.Length);
            if (position < 0 || ordered[position].Type != AttributeType.None)
            {
                ordered.Clear();
                return false;
            }

            ordered[position] = values[i];
        }

        return true;
    }

    /// <summary>Adds a name to a private shape.</summary>
    /// <param name="name">The name.</param>
    /// <param name="position">The number of names so far, which must be less than the length of <see cref="Names"/>.</param>
    public void Append(string name, int position)
    {
        Names[position] = name;
        _index?.Add(name, position);
    }

    /// <summary>Creates the private shape an item continues with after writing into a read item, or to grow.</summary>
    /// <param name="count">The number of names to keep.</param>
    /// <param name="capacity">The length of the new array.</param>
    /// <returns>A shape that only the caller knows.</returns>
    public ItemShape CopyToPrivate(int count, int capacity)
    {
        var copy = CreatePrivate(capacity);
        Names.AsSpan(0, count).CopyTo(copy.Names);
        return copy;
    }

    private static void ThrowOnDuplicates(ReadOnlySpan<string> names)
    {
        if (names.Length <= DuplicateScanLimit)
        {
            for (var i = 1; i < names.Length; i++)
            {
                if (names[..i].Contains(names[i]))
                {
                    throw Duplicate(names[i]);
                }
            }

            return;
        }

        var seen = new HashSet<string>(names.Length, StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!seen.Add(name))
            {
                throw Duplicate(name);
            }
        }
    }

    private static JsonException Duplicate(string name) => new($"The attribute name \"{name}\" occurs more than once.");

    private Dictionary<string, int> BuildIndex(int count)
    {
        var index = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            index[Names[i]] = i;
        }

        // Threads that race here each build the same index, and the last one stays. Assigning a reference publishes a finished object.
        _index = index;
        return index;
    }
}
