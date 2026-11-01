using System.Net;

namespace DynaSharp;

/// <summary>
/// An item collection of a table with local secondary indexes exceeds 10 GB.
/// </summary>
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
