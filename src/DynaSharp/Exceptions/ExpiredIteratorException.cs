using System.Net;

namespace DynaSharp;

/// <summary>
/// A shard iterator is older than the 15 minutes it is valid for. DynamoDB Streams reports it as <c>ExpiredIteratorException</c>.
/// </summary>
/// <remarks>
/// Not retried by the client: sending the same iterator again fails the same way. <see cref="Streams.DynamoDbStreamsClient.ReadShardAsync"/> and
/// <see cref="Streams.DynamoDbStreamsClient.ReadStreamAsync"/> renew an expired iterator by themselves. A loop of your own gets a new iterator with
/// <c>GetShardIterator</c>, using <see cref="Streams.ShardIteratorType.AfterSequenceNumber"/> and the sequence number of the last
/// record processed to resume without gaps.
/// </remarks>
public sealed class ExpiredIteratorException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExpiredIteratorException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB Streams.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ExpiredIteratorException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ExpiredIteratorException", statusCode, requestId)
    {
    }
}
