using System.Net;

namespace DynaSharp;

/// <summary>
/// The condition expression of a write did not hold, so nothing was written. Not retried by the client, because the same request meets the same condition. Code that reads again
/// and decides again can run in <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/>, which repeats it for you.
/// </summary>
public sealed class ConditionalCheckFailedException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConditionalCheckFailedException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="item">The existing item, if the request asked for it.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ConditionalCheckFailedException(string message, Item? item = null, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ConditionalCheckFailedException", statusCode, requestId)
    {
        Item = item;
    }

    /// <summary>
    /// Gets the item the condition was evaluated against, when the request set
    /// <see cref="ReturnValuesOnConditionCheckFailure.AllOld"/> and an item existed; otherwise <see langword="null"/>. On a typed table,
    /// <see cref="DynaSharp.Item.To{T}"/> reads it as the entity.
    /// </summary>
    public Item? Item { get; }
}
