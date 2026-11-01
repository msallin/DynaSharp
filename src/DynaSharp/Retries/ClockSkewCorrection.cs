using System.Collections.Frozen;

namespace DynaSharp.Retries;

/// <summary>
/// Keeps the difference between the local clock and the DynamoDB clock, learned from the <c>Date</c> header of rejected requests.
/// A request signed more than five minutes away from the time of the service is rejected, so a machine with a wrong clock cannot
/// call DynamoDB without this. One instance per client.
/// </summary>
/// <remarks>
/// It is a <see cref="TimeProvider"/> so that the credentials providers a client creates judge expiry on the clock that signs, without
/// knowing about the correction: the expiry of credentials is a time of the service, so comparing it with the local clock
/// refreshes late when that clock is behind. Only the wall clock is corrected. Timers and timestamps measure durations and stay
/// those of the local clock.
/// </remarks>
/// <param name="timeProvider">The local clock.</param>
internal sealed class ClockSkewCorrection(TimeProvider timeProvider) : TimeProvider
{
    // The service tolerates five minutes. The AWS SDKs correct from four, so that a clock that is nearly too far off is fixed
    // before it fails.
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(4);

    // The codes the AWS SDKs treat as a possible sign of clock skew. The signature codes also cover wrong keys, which is why the
    // Date header decides. DynamoDB's own protocol sends InvalidSignatureException for a request signed with the wrong time; the
    // others are the codes of S3, EC2 and the Query protocol. They stay because a proxy, a gateway or an emulator in front of
    // DynamoDB may answer with them, and the SDKs' list is the one that people know.
    private static readonly FrozenSet<string> ClockSkewCodes = FrozenSet.ToFrozenSet(
        [
            "RequestTimeTooSkewed",
            "RequestExpired",
            "RequestInTheFuture",
            "InvalidSignatureException",
            "SignatureDoesNotMatch",
            "AuthFailure",
        ],
        StringComparer.Ordinal);

    private long _offsetTicks;

    /// <inheritdoc/>
    public override long TimestampFrequency => timeProvider.TimestampFrequency;

    /// <summary>Returns whether an error code can mean that the signing clock is off.</summary>
    /// <param name="errorCode">The DynamoDB error code.</param>
    /// <returns><see langword="true"/> for the codes that warrant a look at the <c>Date</c> header.</returns>
    public static bool IsClockSkewCode(string errorCode) => ClockSkewCodes.Contains(errorCode);

    /// <summary>Gets the time of the service as far as it is known: the local clock plus the correction.</summary>
    /// <returns>The time in UTC. Never outside the range of <see cref="DateTimeOffset"/>, whatever the correction.</returns>
    public override DateTimeOffset GetUtcNow()
    {
        // Clamped because a nonsensical Date header must not make signing throw for good.
        var ticks = timeProvider.GetUtcNow().UtcTicks + Volatile.Read(ref _offsetTicks);
        return new DateTimeOffset(Math.Clamp(ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public override long GetTimestamp() => timeProvider.GetTimestamp();

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        timeProvider.CreateTimer(callback, state, dueTime, period);

    /// <summary>
    /// Learns the offset from a rejected request, if the time it was signed with is further from the time of the service than
    /// <see cref="Tolerance"/>. The offset is recomputed from the response every time, never added to the previous one, so a
    /// correction that has become wrong (the clock was fixed in the meantime) heals itself.
    /// </summary>
    /// <param name="signedAt">The time the rejected request was signed with.</param>
    /// <param name="serverTime">The <c>Date</c> header of the response.</param>
    /// <param name="offset">The new offset, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the offset was updated and the request is worth sending again.</returns>
    public bool TryCorrect(DateTimeOffset signedAt, DateTimeOffset serverTime, out TimeSpan offset)
    {
        if ((serverTime - signedAt).Duration() <= Tolerance)
        {
            offset = default;
            return false;
        }

        offset = serverTime - timeProvider.GetUtcNow();
        Volatile.Write(ref _offsetTicks, offset.Ticks);
        return true;
    }
}
