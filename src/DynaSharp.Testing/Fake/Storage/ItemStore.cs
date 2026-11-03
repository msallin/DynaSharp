namespace DynaSharp.Testing.Fake;

/// <summary>
/// The items of a table or of an index, by partition and then by slot, both in the order of <see cref="AttributeComparer"/>. Not thread-safe: the fake holds its gate around every use.
/// </summary>
internal sealed class ItemStore
{
    private readonly SortedDictionary<AttributeValue, SortedList<SlotKey, Item>> _partitions = new(AttributeComparer.Instance);

    /// <summary>Gets the number of items.</summary>
    public int Count { get; private set; }

    /// <summary>Gets an item.</summary>
    /// <param name="partition">The partition key.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The stored item, or <see langword="null"/>.</returns>
    public Item? Get(AttributeValue partition, SlotKey slot) =>
        _partitions.TryGetValue(partition, out var items) && items.TryGetValue(slot, out var item) ? item : null;

    /// <summary>Puts an item, or replaces the one in its slot.</summary>
    /// <param name="partition">The partition key.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="item">The item, which the store keeps as it is.</param>
    public void Set(AttributeValue partition, SlotKey slot, Item item)
    {
        if (!_partitions.TryGetValue(partition, out var items))
        {
            items = new SortedList<SlotKey, Item>(SlotComparer.Instance);
            _partitions.Add(partition, items);
        }

        if (!items.ContainsKey(slot))
        {
            Count++;
        }

        items[slot] = item;
    }

    /// <summary>Removes the item of a slot. A partition without items goes away with its last item.</summary>
    /// <param name="partition">The partition key.</param>
    /// <param name="slot">The slot.</param>
    /// <returns><see langword="true"/> if there was an item.</returns>
    public bool Remove(AttributeValue partition, SlotKey slot)
    {
        if (!_partitions.TryGetValue(partition, out var items) || !items.Remove(slot))
        {
            return false;
        }

        Count--;
        if (items.Count == 0)
        {
            _partitions.Remove(partition);
        }

        return true;
    }

    /// <summary>Enumerates the items of one partition in the order of their slots, or the other way round.</summary>
    /// <param name="partition">The partition key.</param>
    /// <param name="forward"><see langword="false"/> to start at the last slot.</param>
    /// <returns>The slots and their items. The store must not change while it is enumerated.</returns>
    public IEnumerable<KeyValuePair<SlotKey, Item>> Partition(AttributeValue partition, bool forward)
    {
        if (!_partitions.TryGetValue(partition, out var items))
        {
            return [];
        }

        return forward ? items : items.Reverse();
    }

    /// <summary>Enumerates every partition in order, each with its items in the order of their slots.</summary>
    /// <returns>The partitions. The store must not change while it is enumerated.</returns>
    public IEnumerable<KeyValuePair<AttributeValue, SortedList<SlotKey, Item>>> Partitions() => _partitions;

    /// <summary>Orders slots by their three values in turn.</summary>
    internal sealed class SlotComparer : IComparer<SlotKey>
    {
        public static readonly SlotComparer Instance = new();

        private SlotComparer()
        {
        }

        public int Compare(SlotKey x, SlotKey y)
        {
            var order = AttributeComparer.Instance.Compare(x.Sort, y.Sort);
            if (order == 0)
            {
                order = AttributeComparer.Instance.Compare(x.TablePartition, y.TablePartition);
            }

            return order != 0 ? order : AttributeComparer.Instance.Compare(x.TableSort, y.TableSort);
        }
    }
}
