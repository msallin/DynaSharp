using System.Net;

namespace DynaSharp;

/// <summary>
/// A transaction with the same <see cref="Requests.TransactWriteItemsRequest.ClientRequestToken"/> is still running. The client
/// retries it like a throttling error, with the same token, so a retry either finds the first attempt completed or lets it finish.
/// </summary>
public sealed class TransactionInProgressException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionInProgressException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public TransactionInProgressException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "TransactionInProgressException", statusCode, requestId)
    {
    }
}
