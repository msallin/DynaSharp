using System.Net;

namespace DynaSharp;

/// <summary>
/// Control-plane requests, such as <c>CreateTable</c> or <c>DescribeTable</c>, arrive too fast. The client has retried it with backoff, so this reaches the caller when the throttling went on.
/// </summary>
/// <remarks><see cref="DynamoDbException.IsTransient"/> is <see langword="true"/>: wait and try again, and poll a table less often.</remarks>
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
