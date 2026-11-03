namespace DynaSharp.Testing.Fake;

/// <summary>Orders keys by the partition key, then the sort key, with <see cref="AttributeComparer"/>.</summary>
internal sealed class KeyComparer : IComparer<ItemKey>
{
    /// <summary>The comparer.</summary>
    public static readonly KeyComparer Instance = new();

    private KeyComparer()
    {
    }

    /// <inheritdoc/>
    public int Compare(ItemKey x, ItemKey y)
    {
        var order = AttributeComparer.Instance.Compare(x.Partition, y.Partition);
        return order != 0 ? order : AttributeComparer.Instance.Compare(x.Sort ?? AttributeValue.Null, y.Sort ?? AttributeValue.Null);
    }
}
