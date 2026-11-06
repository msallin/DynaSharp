using System.Net;

namespace DynaSharp;

/// <summary>
/// A write was rejected because an item it addresses is being modified by a request in another Region of a global table. The client has retried it, so this reaches the caller when
/// every attempt met the conflict.
/// </summary>
/// <remarks><see cref="DynamoDbException.IsTransient"/> is <see langword="true"/>: try again after a wait.</remarks>
public sealed class ReplicatedWriteConflictException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReplicatedWriteConflictException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ReplicatedWriteConflictException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ReplicatedWriteConflictException", statusCode, requestId)
    {
    }
}
