namespace DynaSharp.Testing.Fake;

/// <summary>What a page of a <c>Query</c> or a <c>Scan</c> is cut with.</summary>
/// <remarks>The filter and the projection are delegates, <see cref="ParsedCondition.Evaluate"/> and <see cref="ParsedProjection.Apply"/> of the request, so the page does not depend on how they were parsed.</remarks>
/// <param name="Limit">The <c>Limit</c>, which counts the items read before the filter, or <see langword="null"/>.</param>
/// <param name="Filter">The <c>FilterExpression</c>, or <see langword="null"/>.</param>
/// <param name="Projection">The <c>ProjectionExpression</c>, or <see langword="null"/>.</param>
/// <param name="Select">The <c>Select</c>, or <see cref="SelectKind.Unspecified"/>.</param>
/// <param name="ConsistentRead"><see langword="true"/> for a strongly consistent read, which costs twice the units.</param>
/// <param name="Table">The table read.</param>
/// <param name="Index">The index read, or <see langword="null"/> for the table.</param>
internal sealed record PageOptions(int? Limit, Func<Item, bool>? Filter, Func<Item, Item>? Projection, SelectKind Select, bool ConsistentRead, FakeTable Table, FakeIndex? Index)
{
    /// <summary>Gets the attributes the filter reads, the first step of each of its paths.</summary>
    public IReadOnlyCollection<string> FilterAttributes { get; init; } = [];

    /// <summary>Gets the attributes the projection names, the first step of each of its paths, in the order of the expression and each once.</summary>
    public IReadOnlyList<string> ProjectionAttributes { get; init; } = [];

    /// <summary>Gets a value indicating whether the read is a <c>Query</c>, whose filter must not read what the index does not project.</summary>
    public bool IsQuery { get; init; }

    /// <summary>
    /// Gets a value indicating whether the read is a <c>Query</c> that ends a page at its last item without <c>LastEvaluatedKey</c>: one whose key condition has a part for the sort key,
    /// or one of a table without a sort key.
    /// </summary>
    public bool KnowsItsEnd { get; init; }
}
