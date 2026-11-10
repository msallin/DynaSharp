using System.Net;

namespace DynaSharp;

/// <summary>
/// A request was rejected because an ongoing transaction includes the item it addresses. Not retried by the client: repeat the request after a short wait, which
/// <see cref="Table{T}.RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken, ConflictKind, int, Action{int, DynamoDbException})"/> does.
/// </summary>
/// <remarks>
/// This is the error of single-item operations and reads. A transaction that conflicts with another one is cancelled with a
/// <see cref="TransactionCanceledException"/> instead, whose reason for the action is <c>TransactionConflict</c>.
/// </remarks>
public sealed class TransactionConflictException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionConflictException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public TransactionConflictException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "TransactionConflictException", statusCode, requestId)
    {
    }
}
