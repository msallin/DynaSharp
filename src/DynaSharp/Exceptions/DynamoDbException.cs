using System.Net;
using DynaSharp.Retries;

namespace DynaSharp;

/// <summary>
/// An error returned by DynamoDB. Specific errors have derived types; all others surface as this type with their
/// <see cref="ErrorCode"/>.
/// </summary>
public class DynamoDbException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="errorCode">The DynamoDB error code without namespace, for example <c>ValidationException</c>.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="requestId">The AWS request id from the <c>x-amzn-RequestId</c> header, for support cases.</param>
    /// <param name="innerException">The underlying failure, if any.</param>
    public DynamoDbException(string message, string errorCode, HttpStatusCode statusCode, string? requestId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>Gets the DynamoDB error code without namespace, for example <c>ValidationException</c>.</summary>
    public string ErrorCode { get; }

    /// <summary>Gets the HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Gets the AWS request id, or <see langword="null"/> if the response had none.</summary>
    public string? RequestId { get; }

    /// <summary>
    /// Gets the DynamoDB operation that failed, for example <c>PutItem</c>. Set by the client on every error that DynamoDB (or a
    /// proxy answering for it) returns; <see langword="null"/> on an exception made elsewhere. The message stays the one DynamoDB
    /// sent, which often does not say what was called.
    /// </summary>
    public string? OperationName { get; internal init; }

    /// <summary>
    /// Gets the table the failed call addressed, exactly as the request named it (a name or an ARN). <see langword="null"/> when
    /// the call has no single table: a batch or transaction (which may span several), <c>ListTables</c>, or a
    /// <c>ListStreams</c> without a table. Never a key or a value.
    /// </summary>
    public string? TableName { get; internal init; }

    /// <summary>
    /// Gets a value indicating whether the same call can succeed later without any change, as <see cref="System.Data.Common.DbException.IsTransient"/> says it for a database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="true"/> for throttling and capacity errors (<see cref="ProvisionedThroughputExceededException"/>, <see cref="ThrottlingException"/>,
    /// <see cref="RequestLimitExceededException"/>, <see cref="LimitExceededException"/>, <see cref="TransactionInProgressException"/>), for a server error, which is
    /// <see cref="InternalServerErrorException"/> and any code with HTTP 500, 502, 503 or 504, a body that is no DynamoDB response included, for the code
    /// <c>RequestTimeoutException</c>, for <see cref="ReplicatedWriteConflictException"/>, for <see cref="TransactionConflictException"/>, and for a
    /// <see cref="TransactionCanceledException"/> whose every failed action is <see cref="TransactionFailure.TransactionConflict"/> or <see cref="TransactionFailure.Throttled"/>.
    /// A <see cref="BatchIncompleteException"/> or <see cref="BatchGetIncompleteException{T}"/> is transient when its rounds ran out on what DynamoDB left unprocessed, and when the
    /// failure in its <see cref="Exception.InnerException"/> is: what is left may pass later.
    /// </para>
    /// <para>
    /// The client already retried every transient failure that its retry policy repeats before the exception reaches the caller, so <see langword="true"/> means that its attempts
    /// ran out, or its retry quota, or that <see cref="DynamoDbClientOptions.MaxAttempts"/> is 1: try later, after a wait, and not at once in a loop. The policy repeats everything
    /// above but the conflicts of a transaction, which <c>RetryOnConflictAsync</c> with <see cref="ConflictKind.TransactionConflict"/> runs again.
    /// </para>
    /// <para>
    /// <see langword="false"/> for everything else: a rejected request (<see cref="ValidationException"/>), a missing or wrong table or index (<see cref="ResourceNotFoundException"/>,
    /// <see cref="ResourceInUseException"/>), access, signature and token errors (<see cref="AccessDeniedException"/>, <see cref="UnrecognizedClientException"/>, and the codes
    /// <c>InvalidSignatureException</c>, <c>MissingAuthenticationTokenException</c> and <c>ExpiredTokenException</c>), an item or item collection over its size limit, an expired
    /// stream iterator, a reused transaction token, and a code this library does not know, unless its status is one of a server error. A failed condition is
    /// <see langword="false"/>, a <see cref="ConditionalCheckFailedException"/> and a cancelled transaction with a <see cref="TransactionFailure.ConditionalCheckFailed"/> reason,
    /// because the same call cannot succeed unless the item changes: read it and decide anew, which <c>RetryOnConflictAsync</c> does.
    /// </para>
    /// <para>
    /// A failure that does not come from DynamoDB is no <see cref="DynamoDbException"/>, so it has no such property: the <see cref="TimeoutException"/> of an attempt and an
    /// <see cref="HttpRequestException"/> of the network are transient and were retried as well, except one whose <see cref="HttpRequestException.HttpRequestError"/> is
    /// <see cref="HttpRequestError.ConfigurationLimitExceeded"/>, and an <see cref="OperationCanceledException"/> and a <see cref="CredentialsUnavailableException"/>, which is also
    /// what a rejection by STS surfaces as, are not.
    /// </para>
    /// </remarks>
    public bool IsTransient => FailureClassifier.IsTransient(this);

    /// <inheritdoc/>
    /// <remarks>
    /// The text of <see cref="Exception.ToString"/>, followed by a line with <see cref="OperationName"/> and <see cref="TableName"/>
    /// when they are known.
    /// </remarks>
    public override string ToString()
    {
        var text = base.ToString();
        if (OperationName is null)
        {
            return text;
        }

        return TableName is null
            ? $"{text}{Environment.NewLine}Operation: {OperationName}"
            : $"{text}{Environment.NewLine}Operation: {OperationName}, table: {TableName}";
    }
}
