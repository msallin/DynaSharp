using System.Net;

namespace DynaSharp;

/// <summary>
/// Too many concurrent control-plane operations, such as tables being created at once, or a resource quota is reached. The client has retried it, so this reaches the caller when every
/// attempt failed.
/// </summary>
/// <remarks>
/// <see cref="DynamoDbException.IsTransient"/> is <see langword="true"/>: try later, after a wait, or raise the quota in the service quotas of the account when it is one.
/// </remarks>
public sealed class LimitExceededException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public LimitExceededException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "LimitExceededException", statusCode, requestId)
    {
    }
}
