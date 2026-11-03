namespace DynaSharp.Testing.Fake;

/// <summary>A condition on the sort key of a key condition. The values have the type of the sort key; the parser of key conditions checks that.</summary>
/// <param name="Operator">The comparison.</param>
/// <param name="Value">The value, the lower end of <see cref="SortKeyOperator.Between"/>, or the prefix of <see cref="SortKeyOperator.BeginsWith"/>.</param>
/// <param name="Upper">The upper end of <see cref="SortKeyOperator.Between"/>, else <see langword="null"/>.</param>
internal sealed record SortKeyRange(SortKeyOperator Operator, AttributeValue Value, AttributeValue? Upper = null)
{
    /// <summary>Tells whether a sort key value satisfies the condition.</summary>
    /// <param name="sortKey">The value, of the type of the sort key.</param>
    /// <returns><see langword="true"/> if it does.</returns>
    public bool Admits(AttributeValue sortKey)
    {
        var order = AttributeComparer.Instance.Compare(sortKey, Value);
        return Operator switch
        {
            SortKeyOperator.Equal => order == 0,
            SortKeyOperator.LessThan => order < 0,
            SortKeyOperator.LessThanOrEqual => order <= 0,
            SortKeyOperator.GreaterThan => order > 0,
            SortKeyOperator.GreaterThanOrEqual => order >= 0,
            SortKeyOperator.Between => order >= 0 && AttributeComparer.Instance.Compare(sortKey, Upper!.Value) <= 0,
            _ => BeginsWith(sortKey, Value),
        };
    }

    // A prefix of characters is a prefix of UTF-8 bytes and the other way round, because UTF-8 never splits a character's bytes over two characters.
    private static bool BeginsWith(AttributeValue sortKey, AttributeValue prefix) => sortKey.Type == AttributeType.String
        ? sortKey.AsString().StartsWith(prefix.AsString(), StringComparison.Ordinal)
        : sortKey.AsBinary().Span.StartsWith(prefix.AsBinary().Span);
}
