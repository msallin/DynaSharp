using Microsoft.Extensions.Logging;

namespace DynaSharp.Coordination;

/// <summary>
/// Log messages of the locks and of <see cref="LockTable.RunAsLeaderAsync"/>, all in the category of <see cref="LockTable"/>. Only what no caller sees is logged: what the
/// heartbeat does in the background (a loss that <see cref="Lease.RenewAsync"/> finds is its answer), a takeover, a release or a contention that failed, and the terms of a leader. A holder is the
/// <see cref="LockTableOptions.HolderLabel"/>, null without one. Event ids are stable and follow those of the client; add new messages with the next free id.
/// </summary>
internal static partial class CoordinationLog
{
    [LoggerMessage(EventId = 40, Level = LogLevel.Warning, Message = "The lease on lock {Lock} in table {Table} is lost ({Reason}).")]
    public static partial void LeaseLost(ILogger logger, string @lock, string table, string reason);

    [LoggerMessage(EventId = 41, Level = LogLevel.Information, Message = "Lock {Lock} in table {Table} was taken over from a holder that did not renew it for its lease of {LeaseDuration} (holder label {PreviousHolder}, now {Holder}).")]
    public static partial void TakenOver(ILogger logger, string @lock, string table, TimeSpan leaseDuration, string? previousHolder, string? holder);

    [LoggerMessage(EventId = 42, Level = LogLevel.Warning, Message = "Renewing the lease on lock {Lock} in table {Table} failed with {ErrorType}. It is tried again in {Delay}, until the lease runs out.")]
    public static partial void RenewRetried(ILogger logger, string @lock, string table, string errorType, TimeSpan delay);

    [LoggerMessage(EventId = 43, Level = LogLevel.Warning, Message = "Releasing the lease on lock {Lock} in table {Table} failed with {ErrorType}. The lock is free once its lease runs out.")]
    public static partial void ReleaseFailed(ILogger logger, string @lock, string table, string errorType);

    [LoggerMessage(EventId = 44, Level = LogLevel.Warning, Message = "Contending for lock {Lock} in table {Table} failed with {ErrorType}. The election contends again every {Delay}, and logs again once DynamoDB answers.")]
    public static partial void ContendFailed(ILogger logger, string @lock, string table, string errorType, TimeSpan delay);

    [LoggerMessage(EventId = 45, Level = LogLevel.Information, Message = "This process leads on lock {Lock} in table {Table}, with fencing token {FencingToken} (holder label {Holder}).")]
    public static partial void Elected(ILogger logger, string @lock, string table, long fencingToken, string? holder);

    [LoggerMessage(EventId = 46, Level = LogLevel.Information, Message = "This process no longer leads on lock {Lock} in table {Table} ({Reason}, holder label {Holder}).")]
    public static partial void TermEnded(ILogger logger, string @lock, string table, string reason, string? holder);

    [LoggerMessage(EventId = 47, Level = LogLevel.Debug, Message = "The work of the leader on lock {Lock} in table {Table} threw {ErrorType} after the lease was lost.")]
    public static partial void WorkFailedAfterLoss(ILogger logger, string @lock, string table, string errorType);

    [LoggerMessage(EventId = 48, Level = LogLevel.Debug, Message = "The work of the leader on lock {Lock} in table {Table} threw {ErrorType} after the caller cancelled.")]
    public static partial void WorkFailedAfterCancel(ILogger logger, string @lock, string table, string errorType);

    [LoggerMessage(EventId = 49, Level = LogLevel.Information, Message = "Contending for lock {Lock} in table {Table} works again, {Elapsed} after it failed and after {Retries} retries.")]
    public static partial void ContendRecovered(ILogger logger, string @lock, string table, TimeSpan elapsed, int retries);
}
