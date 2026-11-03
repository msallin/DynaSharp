namespace DynaSharp.Testing.Fake;

/// <summary>
/// Where an item sits in its partition. A table sorts by its sort key, and a table without one has the NULL value in <see cref="Sort"/>, so each partition holds one item.
/// An index sorts by its own sort key and then by the key of the table, because several items can share a key of an index: <c>(indexSort, tablePartition, tableSort)</c>.
/// </summary>
/// <param name="Sort">The sort key of the table or of the index, or NULL.</param>
/// <param name="TablePartition">For an index, the partition key of the item in the table, else NULL.</param>
/// <param name="TableSort">For an index, the sort key of the item in the table, else NULL.</param>
internal readonly record struct SlotKey(AttributeValue Sort, AttributeValue TablePartition, AttributeValue TableSort)
{
    /// <summary>Gets the slot of an item of a table.</summary>
    /// <param name="key">The key of the item.</param>
    /// <returns>The slot.</returns>
    public static SlotKey OfTable(ItemKey key) => new(key.Sort ?? AttributeValue.Null, AttributeValue.Null, AttributeValue.Null);
}
