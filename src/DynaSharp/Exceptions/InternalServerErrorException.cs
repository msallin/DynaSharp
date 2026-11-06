using System.Net;

namespace DynaSharp;

/// <summary>
/// DynamoDB failed internally (HTTP 500). The client has retried it, so this reaches the caller when every attempt failed.
/// </summary>
/// <remarks>
/// <see cref="DynamoDbException.IsTransient"/> is <see langword="true"/>: try later, after a wait. A write may have been applied before the response was lost, as
/// <see cref="DynamoDbClientOptions.MaxAttempts"/> describes. A proxy that answers 502, 503 or 504 with a page that is no DynamoDB response surfaces as a plain
/// <see cref="DynamoDbException"/> with the code <c>UnrecognizedResponse</c>, retried the same way.
/// </remarks>
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
