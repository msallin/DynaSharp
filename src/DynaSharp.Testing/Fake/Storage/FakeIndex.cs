using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A global or local secondary index: its key, its projection and its own store of projected items. The table keeps it up to date on every write, at once, so a read of an index
/// sees the last write and a backfill takes no time.
/// </summary>
/// <remarks>
/// An item is in the index if it holds every key attribute of the index with the declared type, none empty and none too large (<see cref="KeySchema.Admits"/>). The stored copy holds what the projection says: everything for <c>ALL</c>, the keys of
/// the table and of the index for <c>KEYS_ONLY</c>, and those plus the attributes named for <c>INCLUDE</c>.
/// </remarks>
internal sealed class FakeIndex
{
    private readonly KeySchema _tableKey;

    /// <summary>Initializes a new instance of the <see cref="FakeIndex"/> class, empty.</summary>
    /// <param name="name">The name of the index.</param>
    /// <param name="keySchema">The key of the index.</param>
    /// <param name="projection">What it projects.</param>
    /// <param name="isGlobal"><see langword="true"/> for a global index.</param>
    /// <param name="tableKey">The key of the table.</param>
    public FakeIndex(string name, KeySchema keySchema, IndexProjection projection, bool isGlobal, KeySchema tableKey)
    {
        Name = name;
        KeySchema = keySchema;
        Projection = projection;
        IsGlobal = isGlobal;
        _tableKey = tableKey;
    }

    /// <summary>Gets the name.</summary>
    public string Name { get; }

    /// <summary>Gets the key of the index.</summary>
    public KeySchema KeySchema { get; }

    /// <summary>Gets the projection as it was declared.</summary>
    public IndexProjection Projection { get; }

    /// <summary>Gets a value indicating whether this is a global index.</summary>
    public bool IsGlobal { get; }

    /// <summary>Gets or sets the provisioned read capacity of a global index, 0 on demand.</summary>
    public long ReadCapacityUnits { get; set; }

    /// <summary>Gets or sets the provisioned write capacity of a global index, 0 on demand.</summary>
    public long WriteCapacityUnits { get; set; }

    /// <summary>Gets or sets the on-demand maximums of a global index, as <see cref="FakeTable.OnDemandThroughput"/> holds those of a table.</summary>
    public OnDemandThroughput? OnDemandThroughput { get; set; }

    /// <summary>Gets the number of items in the index.</summary>
    public long ItemCount => Store.Count;

    /// <summary>Gets the size of the projected items.</summary>
    public long SizeBytes => Store.Partitions().Sum(static partition => partition.Value.Values.Sum(ItemSize.Of));

    /// <summary>Gets the store, which the reads of the table go through.</summary>
    internal ItemStore Store { get; } = new();

    /// <summary>Gives what the index holds of an item.</summary>
    /// <param name="item">The item of the table.</param>
    /// <returns>The projected item, a new one unless the projection is <c>ALL</c>.</returns>
    public Item Project(Item item)
    {
        if (Projection.ProjectionType == ProjectionType.All)
        {
            return item;
        }

        var projected = new Item();
        foreach (var name in _tableKey.Names.Concat(KeySchema.Names).Concat(Projection.NonKeyAttributes ?? []))
        {
            if (item.TryGetValue(name, out var value))
            {
                projected[name] = value;
            }
        }

        return projected;
    }

    /// <summary>Tells whether the index holds an attribute of its items: every one for <c>ALL</c>, else the keys of the table and of the index, and the attributes <c>INCLUDE</c> names.</summary>
    /// <param name="attribute">The name of the attribute.</param>
    /// <returns><see langword="true"/> if the projection of the index holds it.</returns>
    public bool Projects(string attribute) =>
        Projection.ProjectionType == ProjectionType.All || _tableKey.Names.Concat(KeySchema.Names).Concat(Projection.NonKeyAttributes ?? []).Contains(attribute, StringComparer.Ordinal);

    /// <summary>Gets the slot of an item in this index.</summary>
    /// <param name="item">The item of the table, which the index covers.</param>
    /// <returns>The partition key of the index and the slot.</returns>
    public (AttributeValue Partition, SlotKey Slot) Locate(IReadOnlyDictionary<string, AttributeValue> item)
    {
        var tableKey = _tableKey.KeyOf(item);
        var sort = KeySchema.SortKeyName is { } sortName ? item[sortName] : AttributeValue.Null;
        return (item[KeySchema.PartitionKeyName], new SlotKey(sort, tableKey.Partition, tableKey.Sort ?? AttributeValue.Null));
    }

    /// <summary>Takes the old image of an item out and puts the new one in, each only where the index admits it.</summary>
    /// <param name="oldImage">The item before the write, or <see langword="null"/>.</param>
    /// <param name="newImage">The item after the write, or <see langword="null"/> for a delete.</param>
    public void Reindex(Item? oldImage, Item? newImage)
    {
        if (oldImage is not null && KeySchema.Admits(oldImage))
        {
            var (partition, slot) = Locate(oldImage);
            Store.Remove(partition, slot);
        }

        if (newImage is not null && KeySchema.Admits(newImage))
        {
            var (partition, slot) = Locate(newImage);
            Store.Set(partition, slot, Project(newImage));
        }
    }

    /// <summary>Fills the index from the items of a table, as a backfill does, at once. An item whose index key is empty or too large stays out.</summary>
    /// <param name="items">Every item of the table.</param>
    public void Backfill(IEnumerable<Item> items)
    {
        foreach (var item in items)
        {
            Reindex(null, item);
        }
    }
}
