using System.Net;

namespace DynaSharp;

/// <summary>
/// The condition expression of a write did not hold, so nothing was written. Not retried by the client, because the same request meets the same condition. Code that reads again
/// and decides again can run in <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/>, which repeats it for you.
/// <see cref="Key"/> says which item it was about.
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

    /// <summary>
    /// Gets the key of the item the condition was checked on, as the request named it (<c>pk</c>, <c>sk</c>), or <see langword="null"/> where the request has no key names. Never in
    /// <see cref="Exception.Message"/>, <see cref="DynamoDbException.ToString"/>, logs or telemetry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DynamoDB does not send the key, so the client takes it from the request: <c>Put</c>, <c>Update</c> and <c>Delete</c> of a table, typed or of <see cref="DynaSharp.Item"/>, and
    /// <see cref="Requests.DynamoDbRequests.UpdateItemAsync"/> and <see cref="Requests.DynamoDbRequests.DeleteItemAsync"/>. A <c>Put</c> reads the key attributes of the table from its item.
    /// It is <see langword="null"/> on <see cref="Requests.DynamoDbRequests.PutItemAsync"/>, whose request does not say which attributes are the key, on <see cref="DynamoDbClient.SendRawAsync"/>, whose JSON is not read, in a
    /// transaction, where <see cref="TransactionCanceledException.ItemFor"/> is the way to an item, and on an exception made elsewhere. It does not depend on
    /// <see cref="ReturnValuesOnConditionCheckFailure.AllOld"/>, which fills <see cref="Item"/> as well.
    /// </para>
    /// <para>
    /// The values are what is stored, such as <c>USER#alice</c>. For a key of a template the generated builders give the inputs back:
    /// <c>Customer.Keys.Pk.Parse(exception.Key!.GetString("pk")!)</c>. A plain key is its input.
    /// </para>
    /// <para>
    /// It is filled on the failure path only, for one parse of the body that was sent, so a write that gets through pays nothing. After the last run of
    /// <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> it is the key of that run.
    /// </para>
    /// </remarks>
    public Item? Key { get; private set; }

    /// <summary>Attaches the key of the request that failed.</summary>
    /// <param name="key">The key attributes.</param>
    internal void Describe(Item key) => Key = key;
}
