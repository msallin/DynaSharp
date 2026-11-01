namespace DynaSharp.Serialization;

/// <summary>
/// Finds the shared <see cref="ItemShape"/> of a read item, so that items with the same attribute names hold them once, across responses
/// and in whatever order the service sends them.
/// </summary>
/// <remarks>
/// <para>
/// Attribute names can be chosen by whoever writes the items, so nothing here grows: the number of slots is fixed, a shape that retains
/// more than <see cref="MaxRetainedBytes"/> is not kept, and a shape that finds no free slot among the few it may use replaces the one at
/// its home slot. When shapes outnumber the slots, a miss costs a names array and a shape object, less than the dictionary an item would
/// be otherwise. An item checks its names for duplicates only on a miss: a hit proves that it has every name of a shape, once.
/// </para>
/// <para>
/// Slots hold shapes that are immutable once published, and a reference is written atomically, so threads need no lock: the worst a race
/// does is lose an entry, or create a shape twice.
/// </para>
/// </remarks>
/// <param name="slots">The number of slots, a power of two of at least <see cref="NameTable.Probes"/>.</param>
internal sealed class ShapeTable(int slots)
{
    // Keeps the table below slots * 8 KB whatever the names are. About 80 attributes of 20 characters fit, and a larger item still
    // shares its shape with the rest of its page.
    private const int MaxRetainedBytes = 8 * 1024;

    private readonly ItemShape?[] _shapes = new ItemShape?[slots];

    /// <summary>Gets the table production code uses, shared by all clients. At most 16 MB, with every slot on a shape of the largest kind; typically some KB.</summary>
    public static ShapeTable Shared { get; } = new(2048);

    /// <summary>Builds the item from what was read, in the order of its shape.</summary>
    /// <param name="names">The names as read, possibly with a duplicate.</param>
    /// <param name="values">The values as read.</param>
    /// <param name="previous">The shape of the previous item of the page, tried first because it needs no shared state.</param>
    /// <returns>The item.</returns>
    /// <exception cref="System.Text.Json.JsonException">A name occurs twice.</exception>
    public Item CreateItem(ReadOnlySpan<string> names, ReadOnlySpan<AttributeValue> values, ItemShape previous)
    {
        var ordered = new AttributeValue[names.Length];
        var hash = ItemShape.HashNames(names);
        if (previous.NameHash == hash && previous.TryOrder(names, values, ordered))
        {
            return new Item(previous, ordered);
        }

        var mask = _shapes.Length - 1;
        var home = hash & mask;
        var target = home;
        for (var probe = 0; probe < NameTable.Probes; probe++)
        {
            var slot = (home + probe) & mask;
            var cached = _shapes[slot];
            if (cached is null)
            {
                target = slot;
                break;
            }

            if (cached.NameHash == hash && cached.TryOrder(names, values, ordered))
            {
                return new Item(cached, ordered);
            }
        }

        var shape = ItemShape.CreateShared(names, hash);
        if (shape.RetainedBytes <= MaxRetainedBytes)
        {
            _shapes[target] = shape;
        }

        values.CopyTo(ordered);
        return new Item(shape, ordered);
    }
}
