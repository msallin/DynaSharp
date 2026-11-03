namespace DynaSharp.Testing.Fake;

/// <summary>A page of a <c>Query</c> or a <c>Scan</c>.</summary>
/// <param name="Items">The items, after the filter and the projection; empty for <c>Select: COUNT</c>.</param>
/// <param name="Count">The items after the filter.</param>
/// <param name="ScannedCount">The items read before the filter.</param>
/// <param name="LastEvaluatedKey">The key to go on after, or <see langword="null"/> at the end.</param>
internal sealed record Page(IReadOnlyList<Item> Items, int Count, int ScannedCount, Item? LastEvaluatedKey);
