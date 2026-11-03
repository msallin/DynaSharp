namespace DynaSharp.Testing.Fake;

/// <summary>What a page of a <c>Query</c> or a <c>Scan</c> is cut with.</summary>
/// <param name="Limit">The <c>Limit</c>, which counts the items read before the filter, or <see langword="null"/>.</param>
/// <param name="Filter">The <c>FilterExpression</c>, or <see langword="null"/>.</param>
/// <param name="Projection">The <c>ProjectionExpression</c>, or <see langword="null"/>.</param>
/// <param name="Select">The <c>Select</c>, or <see cref="SelectKind.Unspecified"/>.</param>
/// <param name="Table">The table read.</param>
/// <param name="Index">The index read, or <see langword="null"/> for the table.</param>
internal sealed record PageOptions(int? Limit, ParsedCondition? Filter, ParsedProjection? Projection, SelectKind Select, FakeTable Table, FakeIndex? Index);
