using System.Net;

namespace DynaSharp;

/// <summary>
/// DynamoDB failed internally. Retried automatically before it surfaces.
/// </summary>
public sealed class InternalServerErrorException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InternalServerErrorException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public InternalServerErrorException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
        : base(message, "InternalServerError", statusCode, requestId)
    {
    }
}
