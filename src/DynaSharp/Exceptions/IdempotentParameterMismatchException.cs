using System.Net;

namespace DynaSharp;

/// <summary>
/// A transaction was sent with a <see cref="Requests.TransactWriteItemsRequest.ClientRequestToken"/> that DynamoDB saw within the last
/// 10 minutes with other parameters. Not retried: the same request meets the same answer. Use a new token for a different transaction.
/// </summary>
public sealed class IdempotentParameterMismatchException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotentParameterMismatchException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public IdempotentParameterMismatchException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "IdempotentParameterMismatchException", statusCode, requestId)
    {
    }
}
