namespace DynaSharp.Testing.Fake;

/// <summary>A page of a <c>Query</c> or a <c>Scan</c>.</summary>
/// <param name="Items">The items, after the filter and the projection; empty for <c>Select: COUNT</c>.</param>
/// <param name="Count">The items after the filter.</param>
/// <param name="ScannedCount">The items read before the filter.</param>
/// <param name="LastEvaluatedKey">The key to go on after, or <see langword="null"/> at the end.</param>
/// <param name="Units">The read units of the table or the index read.</param>
/// <param name="FetchUnits">The read units of the items a local index fetched from its table, 0 for any other read.</param>
internal sealed record Page(IReadOnlyList<Item> Items, int Count, int ScannedCount, Item? LastEvaluatedKey, double Units, double FetchUnits);
