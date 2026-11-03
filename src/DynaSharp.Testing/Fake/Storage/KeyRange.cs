namespace DynaSharp.Testing.Fake;

/// <summary>What a key condition selects: one partition, and the sort keys of it that <see cref="Sort"/> admits, or all of them.</summary>
/// <param name="Partition">The value of the partition key, in its stored form.</param>
/// <param name="Sort">The condition on the sort key, or <see langword="null"/> for every item of the partition.</param>
internal sealed record KeyRange(AttributeValue Partition, SortKeyRange? Sort);
