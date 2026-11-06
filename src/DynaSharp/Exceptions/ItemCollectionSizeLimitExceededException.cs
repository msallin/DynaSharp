using System.Net;

namespace DynaSharp;

/// <summary>
/// An item collection, all the items with one partition key value, exceeds the 10 GB that a table with a local secondary index allows. Not retried: data must go first.
/// </summary>
/// <remarks>
/// A table without a local secondary index has no such limit. <see cref="DynamoDbException.IsTransient"/> is <see langword="false"/>. <c>ReturnMetrics()</c> on a write asks for the size
/// estimate of the collection, so that a service can see the limit coming.
/// </remarks>
public sealed class ItemCollectionSizeLimitExceededException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemCollectionSizeLimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ItemCollectionSizeLimitExceededException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ItemCollectionSizeLimitExceededException", statusCode, requestId)
    {
    }
}
