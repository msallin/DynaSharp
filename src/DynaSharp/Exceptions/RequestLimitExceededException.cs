using System.Net;

namespace DynaSharp;

/// <summary>
/// The account-level throughput quota is exceeded. The client has retried it with backoff, so this reaches the caller when the account stayed over its quota.
/// </summary>
/// <remarks><see cref="DynamoDbException.IsTransient"/> is <see langword="true"/>: wait and try again, or raise the quota in the service quotas of the account.</remarks>
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
