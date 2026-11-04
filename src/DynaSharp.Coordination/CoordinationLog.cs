using Microsoft.Extensions.Logging;

namespace DynaSharp.Coordination;

/// <summary>
/// Log messages of the locks and of <see cref="LeaderElection"/>, all in the category of <see cref="LockTable"/>. Only what no caller sees is logged: what the
/// heartbeat does in the background, a takeover, a release or a contention that failed, and the terms of a leader. Event ids are stable and follow those of the client; add new
/// messages with the next free id.
/// </summary>
internal static partial class CoordinationLog
{
    [LoggerMessage(EventId = 40, Level = LogLevel.Warning, Message = "The lease on lock {Lock} in table {Table} is lost ({Reason}).")]
    public static partial void LeaseLost(ILogger logger, string @lock, string table, string reason);

    [LoggerMessage(EventId = 41, Level = LogLevel.Information, Message = "Lock {Lock} in table {Table} was taken over from a holder that did not renew it for its lease of {LeaseDuration}.")]
    public static partial void TakenOver(ILogger logger, string @lock, string table, TimeSpan leaseDuration);

    [LoggerMessage(EventId = 42, Level = LogLevel.Warning, Message = "Renewing the lease on lock {Lock} in table {Table} failed with {ErrorType}. It is tried again in {Delay}, until the lease runs out.")]
    public static partial void RenewRetried(ILogger logger, string @lock, string table, string errorType, TimeSpan delay);

    [LoggerMessage(EventId = 43, Level = LogLevel.Warning, Message = "Releasing the lease on lock {Lock} in table {Table} failed with {ErrorType}. The lock is free once its lease runs out.")]
    public static partial void ReleaseFailed(ILogger logger, string @lock, string table, string errorType);

    [LoggerMessage(EventId = 44, Level = LogLevel.Warning, Message = "Contending for lock {Lock} in table {Table} failed with {ErrorType}. The election contends again in {Delay}.")]
    public static partial void ContendFailed(ILogger logger, string @lock, string table, string errorType, TimeSpan delay);

    [LoggerMessage(EventId = 45, Level = LogLevel.Information, Message = "This process leads on lock {Lock} in table {Table}, with fencing token {FencingToken}.")]
    public static partial void Elected(ILogger logger, string @lock, string table, long fencingToken);

    [LoggerMessage(EventId = 46, Level = LogLevel.Information, Message = "This process no longer leads on lock {Lock} in table {Table} ({Reason}).")]
    public static partial void TermEnded(ILogger logger, string @lock, string table, string reason);

    [LoggerMessage(EventId = 47, Level = LogLevel.Debug, Message = "The work of the leader on lock {Lock} in table {Table} threw {ErrorType} after the lease was lost.")]
    public static partial void WorkFailedAfterLoss(ILogger logger, string @lock, string table, string errorType);
}
