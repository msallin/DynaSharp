namespace DynaSharp.Requests;

/// <summary>
/// The Time to Live setting of a table: whether DynamoDB deletes expired items, and which attribute holds the expiry time.
/// </summary>
/// <remarks>
/// The attribute of an item must be a number holding a Unix timestamp in seconds. Items without the attribute, or with another type
/// of value, never expire.
/// </remarks>
public sealed record TimeToLiveSpecification
{
    /// <summary>Gets a value indicating whether expired items are deleted.</summary>
    public required bool Enabled { get; init; }

    /// <summary>
    /// Gets the name of the attribute with the expiry time, 1 to 255 characters. It cannot be changed while Time to Live is on; turn
    /// it off and on again with the new name.
    /// </summary>
    public required string AttributeName { get; init; }
}
