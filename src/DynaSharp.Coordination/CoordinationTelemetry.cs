using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DynaSharp.Coordination;

/// <summary>
/// Spans and metrics of the locks, under the activity source and the meter named <c>DynaSharp</c> that the client reports to, so that one subscription
/// covers both. A span of a lock is the parent of the spans of the requests it sends. Without a listener a span costs a null check, and a measurement the check inside the counter.
/// </summary>
internal static class CoordinationTelemetry
{
    public const string Source = "DynaSharp";

    public const string TryAcquireSpan = "Lock.TryAcquire";
    public const string AcquireSpan = "Lock.Acquire";
    public const string RenewSpan = "Lock.Renew";
    public const string ReleaseSpan = "Lock.Release";

    public const string AcquisitionsMetric = "dynasharp.lock.acquisitions";
    public const string RenewalsMetric = "dynasharp.lock.renewals";
    public const string LossesMetric = "dynasharp.lock.losses";

    public const string DbSystemName = "db.system.name";
    public const string DbSystemNameValue = "aws.dynamodb";
    public const string DbCollectionName = "db.collection.name";
    public const string ErrorType = "error.type";
    public const string LockName = "dynasharp.lock.name";
    public const string Acquired = "dynasharp.lock.acquired";
    public const string TakeOver = "dynasharp.lock.takeover";
    public const string FencingToken = "dynasharp.lock.fencing_token";
    public const string LossReason = "dynasharp.lock.loss_reason";

    private static readonly ActivitySource ActivitySource = new(Source);

    private static readonly Meter Meter = new(Source);

    private static readonly Counter<long> Acquisitions = Meter.CreateCounter<long>(
        AcquisitionsMetric,
        unit: "{lease}",
        description: "Number of leases a LockTable acquired, also by taking over a lock whose holder stopped renewing it.");

    private static readonly Counter<long> Renewals = Meter.CreateCounter<long>(
        RenewalsMetric,
        unit: "{renewal}",
        description: "Number of renewals of leases, by the heartbeat or by RenewAsync, with error.type on the failed ones.");

    private static readonly Counter<long> Losses = Meter.CreateCounter<long>(
        LossesMetric,
        unit: "{lease}",
        description: "Number of leases lost before they were released: taken over, run out, or given up after a failed renewal.");

    /// <summary>Starts the span of an operation on a lock.</summary>
    /// <param name="name">The name of the span.</param>
    /// <param name="table">The table of the lock.</param>
    /// <param name="lockName">The name of the lock.</param>
    /// <returns>The span, or <see langword="null"/> without a listener.</returns>
    public static Activity? Start(string name, string table, string lockName) =>
        ActivitySource.StartActivity(name, ActivityKind.Internal, default(ActivityContext), [new(DbSystemName, DbSystemNameValue), new(DbCollectionName, table), new(LockName, lockName)]);

    /// <summary>Records the outcome of an acquire on its span and in the metrics.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="lease">The lease, or <see langword="null"/> if the lock is held.</param>
    /// <param name="takeOver">Whether the lease was taken over from a holder that stopped renewing.</param>
    public static void Acquire(Activity? activity, Lease? lease, bool takeOver)
    {
        activity?.SetTag(Acquired, lease is not null);
        if (lease is null)
        {
            return;
        }

        activity?.SetTag(TakeOver, takeOver);
        activity?.SetTag(FencingToken, lease.FencingToken);
        Acquisitions.Add(1, new(DbCollectionName, lease.Locks.Table.Name), new(LockName, lease.Name), new(TakeOver, takeOver));
    }

    /// <summary>Records a renewal in the metrics.</summary>
    /// <param name="lease">The lease.</param>
    /// <param name="errorType">The error, or <see langword="null"/> for a renewal that succeeded.</param>
    public static void Renew(Lease lease, string? errorType)
    {
        if (errorType is null)
        {
            Renewals.Add(1, new(DbCollectionName, lease.Locks.Table.Name), new(LockName, lease.Name));
        }
        else
        {
            Renewals.Add(1, new(DbCollectionName, lease.Locks.Table.Name), new(LockName, lease.Name), new(ErrorType, errorType));
        }
    }

    /// <summary>Records the loss of a lease in the metrics.</summary>
    /// <param name="lease">The lease.</param>
    /// <param name="reason">Why it was lost.</param>
    public static void Lose(Lease lease, string reason)
    {
        Losses.Add(1, new(DbCollectionName, lease.Locks.Table.Name), new(LockName, lease.Name), new(LossReason, reason));
    }

    /// <summary>Marks a span as failed, with the error code of DynamoDB or the type of the exception, never the message.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="exception">The exception.</param>
    public static void Fail(Activity? activity, Exception exception)
    {
        if (activity is not null)
        {
            activity.SetTag(ErrorType, ErrorTypeOf(exception));
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    /// <summary>Gets what <c>error.type</c> says about an exception: the code DynamoDB sent, or the full name of the type.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The error type.</returns>
    public static string ErrorTypeOf(Exception exception) => exception is DynamoDbException dynamo ? dynamo.ErrorCode : exception.GetType().FullName!;
}
