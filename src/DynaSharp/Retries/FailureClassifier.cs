using System.Collections.Frozen;
using System.Net;

namespace DynaSharp.Retries;

/// <summary>
/// Sorts the exception of a failed attempt into a <see cref="FailureKind"/>. The rules are those of the AWS "standard" retry mode
/// (<c>botocore.retries.standard</c>), reduced to what DynamoDB's JSON protocol can produce. The remarks of
/// <see cref="DynamoDbClientOptions.MaxAttempts"/> are the public description of these rules: a change here changes them too.
/// </summary>
internal static class FailureClassifier
{
    // botocore ThrottledRetryableChecker, without the codes of other services and protocols (SlowDown: S3, PriorRequestNotComplete:
    // Route 53, EC2ThrottledException: EC2, Throttling and similar: Query protocol). TransactionInProgressException is in botocore's list;
    // a retry carries the same ClientRequestToken, which is what makes it safe. TransactionCanceledException, TransactionConflictException
    // and IdempotentParameterMismatchException are not in any list: the caller decides about them.
    private static readonly FrozenSet<string> ThrottlingCodes = FrozenSet.ToFrozenSet(
        [
            "ThrottlingException",
            "ProvisionedThroughputExceededException",
            "TransactionInProgressException",
            "RequestLimitExceeded",
            "LimitExceededException",
        ],
        StringComparer.Ordinal);

    // botocore TransientRetryableChecker (RequestTimeoutException; RequestTimeout is S3's) plus the one DynamoDB error that its service
    // model marks retryable without throttling.
    private static readonly FrozenSet<string> TransientCodes = FrozenSet.ToFrozenSet(
        [
            "RequestTimeoutException",
            "ReplicatedWriteConflictException",
        ],
        StringComparer.Ordinal);

    // botocore TransientRetryableChecker. A proxy that answers 503 with HTML ends up here too, as UnrecognizedResponse.
    private static readonly FrozenSet<int> TransientStatusCodes = FrozenSet.ToFrozenSet(
        [(int)HttpStatusCode.InternalServerError, (int)HttpStatusCode.BadGateway, (int)HttpStatusCode.ServiceUnavailable, (int)HttpStatusCode.GatewayTimeout]);

    /// <summary>Returns whether an error code says that the token of the request has expired.</summary>
    /// <param name="errorCode">The DynamoDB error code.</param>
    /// <returns><see langword="true"/> for the codes after which new credentials can make the same request work.</returns>
    /// <remarks>
    /// botocore has no such list: it refreshes by time alone, and neither its retry rules nor the DynamoDB service model name these codes.
    /// The source is the list of the AWS SDK for Go, <c>credsExpiredCodes</c>: <c>ExpiredTokenException</c> is what DynamoDB sends, and
    /// <c>ExpiredToken</c> the form of S3 and the Query protocol, kept as <see cref="ClockSkewCorrection"/> keeps such codes because a proxy
    /// in front of DynamoDB may answer with it. Its third code, <c>RequestExpired</c>, is about the age of a request and not about the
    /// credentials, so it stays with the clock.
    /// </remarks>
    public static bool IsExpiredCredentialsCode(string errorCode) => errorCode is "ExpiredTokenException" or "ExpiredToken";

    /// <summary>Classifies the exception an attempt failed with.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>Whether and how the request may be repeated.</returns>
    public static FailureKind Classify(Exception exception) => exception switch
    {
        DynamoDbException dynamoDbException => ClassifyResponse(dynamoDbException.ErrorCode, dynamoDbException.StatusCode),

        // Also a timeout of the HTTP handler, such as its connect timeout, which the pipeline turns into one. botocore charges it as a
        // timeout (ConnectTimeoutError) too.
        TimeoutException => FailureKind.Timeout,

        // A limit of the client was exceeded, for example a response body above the ceiling. The same request gets the same response.
        HttpRequestException { HttpRequestError: HttpRequestError.ConfigurationLimitExceeded } => FailureKind.Permanent,
        HttpRequestException => FailureKind.Transient,
        _ => FailureKind.Permanent,
    };

    /// <summary>
    /// Tells whether a failure means that the operation lost to another writer, so that reading again and writing again can pass. This is what
    /// <see cref="Table{T}.RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken, ConflictKind, int, Action{int, DynamoDbException})"/> runs its delegate again on. It has nothing to do with
    /// <see cref="Classify"/>: to the pipeline all of these are permanent, because the same request meets the same condition.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="kinds">The conflicts that count, at least one.</param>
    /// <returns>
    /// <see langword="true"/> for a failed condition and an ongoing transaction, each when <paramref name="kinds"/> holds it, and for a cancelled transaction with one of those among its reasons, whatever the others say.
    /// </returns>
    public static bool IsConflict(DynamoDbException exception, ConflictKind kinds) => exception switch
    {
        ConditionalCheckFailedException => kinds.HasFlag(ConflictKind.ConditionalCheckFailed),
        TransactionConflictException => kinds.HasFlag(ConflictKind.TransactionConflict),
        TransactionCanceledException canceled => HasConflictReason(canceled.Failures, kinds),
        _ => false,
    };

    /// <summary>
    /// Tells whether the same call can pass later without any change, which <see cref="DynamoDbException.IsTransient"/> answers. Everything that <see cref="Classify"/> repeats
    /// is, so the two cannot disagree, and so are the conflicts of a transaction, which the pipeline leaves to the caller: the other transaction ends without anything changing on this side.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>
    /// <see langword="true"/> for what the pipeline repeats, for <see cref="TransactionConflictException"/>, for a cancelled transaction whose every failed action is a conflict or
    /// throttled, and for a batch that gave up on its rounds or on a transient failure.
    /// </returns>
    public static bool IsTransient(DynamoDbException exception) => exception switch
    {
        TransactionConflictException => true,
        TransactionCanceledException canceled => HasOnlyPassingReasons(canceled.Failures),

        // BatchIncompleteException and BatchGetIncompleteException<T> share the code. Without a cause the rounds ran out on what DynamoDB left unprocessed, which is throttling.
        { ErrorCode: BatchIncompleteException.Code } => exception.InnerException switch
        {
            null => true,
            DynamoDbException cause => IsTransient(cause),
            var cause => Classify(cause) != FailureKind.Permanent,
        },
        _ => ClassifyResponse(exception.ErrorCode, exception.StatusCode) != FailureKind.Permanent,
    };

    private static FailureKind ClassifyResponse(string errorCode, HttpStatusCode statusCode)
    {
        if (ThrottlingCodes.Contains(errorCode))
        {
            return FailureKind.Throttling;
        }

        return TransientCodes.Contains(errorCode) || TransientStatusCodes.Contains((int)statusCode) ? FailureKind.Transient : FailureKind.Permanent;
    }

    // When adding a member to TransactionFailure: say here whether the same transaction can pass later. A transaction without reasons, or with only None, says nothing, so it is not transient.
    private static bool HasOnlyPassingReasons(IReadOnlyList<TransactionActionFailure> failures)
    {
        foreach (var failure in failures)
        {
            if (failure.Reason is not (TransactionFailure.TransactionConflict or TransactionFailure.Throttled))
            {
                return false;
            }
        }

        return failures.Count > 0;
    }

    private static bool HasConflictReason(IReadOnlyList<TransactionActionFailure> failures, ConflictKind kinds)
    {
        foreach (var failure in failures)
        {
            var kind = failure.Reason switch
            {
                TransactionFailure.ConditionalCheckFailed => ConflictKind.ConditionalCheckFailed,
                TransactionFailure.TransactionConflict => ConflictKind.TransactionConflict,
                _ => default,
            };

            if ((kind & kinds) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
