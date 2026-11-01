using System.Net;

namespace DynaSharp;

/// <summary>
/// The table or index is being created, updated or deleted, or already exists.
/// </summary>
public sealed class ResourceInUseException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceInUseException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ResourceInUseException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ResourceInUseException", statusCode, requestId)
    {
    }
}
