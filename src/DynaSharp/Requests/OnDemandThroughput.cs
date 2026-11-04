namespace DynaSharp.Requests;

/// <summary>
/// The maximum throughput of a table or a global secondary index with <see cref="BillingMode.PayPerRequest"/> capacity: requests above it are throttled.
/// </summary>
/// <remarks>
/// <para>
/// Without a maximum, on-demand throughput is bounded only by the account's quota per table, 40,000 read and 40,000 write request units by default. A maximum
/// is at least 1 and at most that quota, and DynamoDB applies it on a best-effort basis: burst capacity can let a few requests above it through.
/// </para>
/// <para>
/// In a request, set <see cref="MaxReadRequestUnits"/>, <see cref="MaxWriteRequestUnits"/> or both: a member that is <see langword="null"/> is not sent and,
/// in <c>UpdateTable</c>, keeps its current value. <c>-1</c> removes a maximum. DynamoDB rejects a maximum on a table or index with
/// <see cref="BillingMode.Provisioned"/> capacity, and a table that switches to provisioned capacity loses its maximums.
/// </para>
/// <para>
/// In a description, <see langword="null"/> or <c>-1</c> means that there is no maximum; DynamoDB may leave the whole object out when neither has one.
/// </para>
/// </remarks>
public sealed record OnDemandThroughput
{
    /// <summary>
    /// Gets the maximum read request units per second: at least 1, or <c>-1</c> in a request to remove the maximum. <see langword="null"/> sends nothing,
    /// and in a description means, as <c>-1</c> does, that reads have no maximum.
    /// </summary>
    public long? MaxReadRequestUnits { get; init; }

    /// <summary>
    /// Gets the maximum write request units per second: at least 1, or <c>-1</c> in a request to remove the maximum. <see langword="null"/> sends nothing,
    /// and in a description means, as <c>-1</c> does, that writes have no maximum.
    /// </summary>
    public long? MaxWriteRequestUnits { get; init; }
}
