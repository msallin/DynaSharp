using System.Net;

namespace DynaSharp;

/// <summary>
/// The credentials are valid but lack permission for the operation.
/// </summary>
public sealed class AccessDeniedException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AccessDeniedException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public AccessDeniedException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "AccessDeniedException", statusCode, requestId)
    {
    }
}
