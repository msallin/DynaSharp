using System.Net;

namespace DynaSharp;

/// <summary>
/// Too many concurrent control-plane operations, or a resource quota is reached. Retried automatically before it surfaces.
/// </summary>
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
