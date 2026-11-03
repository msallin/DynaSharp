namespace DynaSharp.Testing.Fake;

/// <summary>The key of an item: the partition key value and the sort key value, if the table has a sort key. Both are in their stored form, so equal keys are equal values.</summary>
/// <param name="Partition">The value of the partition key.</param>
/// <param name="Sort">The value of the sort key, or <see langword="null"/> for a table without one.</param>
internal readonly record struct ItemKey(AttributeValue Partition, AttributeValue? Sort);
