namespace DynaSharp.Requests;

/// <summary>
/// Turns Time to Live on a table on or off (DynamoDB <c>UpdateTimeToLive</c>).
/// </summary>
/// <remarks>
/// <para>
/// With Time to Live on, DynamoDB deletes the items whose attribute named in <see cref="TimeToLiveSpecification"/> holds an epoch
/// time in seconds that has passed. Deletion is a background process without a deadline: it typically happens within a few days of
/// the expiry, not at the moment. Until then expired items are still returned by reads, queries and scans; filter on the expiry
/// attribute if that matters. The deletion is free and shows up in the stream of the table as a removal by the DynamoDB service.
/// </para>
/// <para>
/// A change takes up to an hour to take effect, and the setting of a table can be changed again only after that hour.
/// </para>
/// </remarks>
public sealed record UpdateTimeToLiveRequest
{
    /// <summary>Gets the table name (or table ARN).</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the new setting.</summary>
    public required TimeToLiveSpecification TimeToLiveSpecification { get; init; }
}
