using System.Net;

namespace DynaSharp;

/// <summary>
/// The account-level throughput quota is exceeded. Retried automatically before it surfaces.
/// </summary>
public sealed class RequestLimitExceededException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestLimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public RequestLimitExceededException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "RequestLimitExceeded", statusCode, requestId)
    {
    }
}
