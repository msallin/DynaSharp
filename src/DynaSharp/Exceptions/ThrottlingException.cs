using System.Net;

namespace DynaSharp;

/// <summary>
/// Control-plane requests (for example <c>CreateTable</c>) arrive too fast. Retried automatically before it surfaces.
/// </summary>
public sealed class ThrottlingException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottlingException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ThrottlingException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ThrottlingException", statusCode, requestId)
    {
    }
}
