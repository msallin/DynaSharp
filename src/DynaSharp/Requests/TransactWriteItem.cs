namespace DynaSharp.Requests;

/// <summary>
/// One action of a <see cref="TransactWriteItemsRequest"/>: a put, a delete, an update or a condition check.
/// </summary>
/// <remarks>
/// Set exactly one of <see cref="ConditionCheck"/>, <see cref="Put"/>, <see cref="Delete"/> and <see cref="Update"/>. The library
/// sends whatever is set and leaves the check to DynamoDB, which rejects an action with none or with several members with a
/// <see cref="ValidationException"/>.
/// </remarks>
public sealed record TransactWriteItem
{
    /// <summary>Gets the condition check, or <see langword="null"/> if this action is another kind.</summary>
    public TransactConditionCheck? ConditionCheck { get; init; }

    /// <summary>Gets the put, or <see langword="null"/> if this action is another kind.</summary>
    public TransactPut? Put { get; init; }

    /// <summary>Gets the delete, or <see langword="null"/> if this action is another kind.</summary>
    public TransactDelete? Delete { get; init; }

    /// <summary>Gets the update, or <see langword="null"/> if this action is another kind.</summary>
    public TransactUpdate? Update { get; init; }
}
