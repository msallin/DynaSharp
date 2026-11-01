namespace DynaSharp.Streams;

/// <summary>
/// The identity behind a change that DynamoDB made itself.
/// </summary>
public sealed record RecordIdentity
{
    /// <summary>Gets the identifier of the principal: <c>dynamodb.amazonaws.com</c> for deletions by Time to Live.</summary>
    public string? PrincipalId { get; init; }

    /// <summary>Gets the type of the principal: <c>Service</c> for deletions by Time to Live.</summary>
    public string? Type { get; init; }
}
