namespace DynaSharp.Requests;

/// <summary>
/// One action of a <see cref="TransactGetItemsRequest"/>.
/// </summary>
public sealed record TransactGetItem
{
    /// <summary>Gets the item to read.</summary>
    public required TransactGet Get { get; init; }
}
