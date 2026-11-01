using System.Net;

namespace DynaSharp;

/// <summary>
/// The records a request asks for are gone: they are older than the 24 hours that DynamoDB Streams keeps them.
/// DynamoDB Streams reports it as <c>TrimmedDataAccessException</c>.
/// </summary>
/// <remarks>
/// Not retried by the client. A reader that fell behind by more than a day lost those changes for good; start again at
/// <see cref="Streams.ShardIteratorType.TrimHorizon"/>, the oldest record that is left, and rebuild the state from the table if the
/// gap matters.
/// </remarks>
public sealed class TrimmedDataAccessException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrimmedDataAccessException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB Streams.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public TrimmedDataAccessException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "TrimmedDataAccessException", statusCode, requestId)
    {
    }
}
