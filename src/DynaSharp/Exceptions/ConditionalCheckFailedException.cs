using System.Net;
using DynaSharp.Fluent;

namespace DynaSharp;

/// <summary>
/// The condition expression of a write did not hold, so nothing was written. Not retried by the client, because the same request meets the same condition. Code that reads again
/// and decides again can run in <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/>, which repeats it for you.
/// <see cref="Key"/> says which item it was about.
/// </summary>
/// <remarks>
/// DynamoDB says no more than "The conditional request failed". After a <c>Put</c>, <c>Update</c> or <c>Delete</c> of a table, <see cref="Exception.Message"/> gains a line that says which condition the
/// write sent: the version check, named by its attribute, the check that no item or that an item has the key, or the caller's own condition, all of them when there were several, because DynamoDB does not
/// say which part failed. A version that marks a new item says so, with the two ways to write over the stored one. The line names attributes and the table and never a value, so the exception is as safe
/// to log as any other.
/// </remarks>
public sealed class ConditionalCheckFailedException : DynamoDbException
{
    // What names the condition, kept as it is until the message is read: in a create-if-absent a failed condition is routine, and code that catches it never reads the text.
    private ConditionParts _parts;
    private string? _operation;
    private string? _tableName;
    private string? _versionAttributeName;
    private string? _explanation;

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

    /// <summary>Gets the message of the service, followed for a command by a line that says which condition the write sent.</summary>
    public override string Message => _parts == ConditionParts.None
        ? base.Message
        : $"{base.Message}{Environment.NewLine}{_explanation ??= FailedCondition.Line(_operation!, _tableName!, _versionAttributeName, _parts)}";

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
    /// A caller that holds the key gains nothing from this, because it is the key that caller passed. It is for code that does not hold it: a wrapper around a delegate, a handler at the edge of a service, the last run of a
    /// retry. The client puts it in no log, so what such code logs is its own decision.
    /// </para>
    /// <code>
    /// catch (ConditionalCheckFailedException exception) when (exception.Key is { } key)
    /// {
    ///     var customerId = Customer.Keys.Pk.Parse(key.GetString("pk")!).Id;
    ///     return Results.Conflict(customerId);
    /// }
    /// </code>
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
    /// It is taken from the request only when the condition fails. After the last run of
    /// <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> it is the key of that run.
    /// </para>
    /// </remarks>
    public Item? Key { get; private set; }

    /// <summary>Attaches the key of the request that failed.</summary>
    /// <param name="key">The key attributes.</param>
    internal void Describe(Item key) => Key = key;

    /// <summary>Attaches what the line that says which condition the write sent is made of, see <see cref="FailedCondition.Line"/>.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="tableName">The table.</param>
    /// <param name="versionAttributeName">The version attribute of the table, if it has one.</param>
    /// <param name="parts">The parts of the condition, not <see cref="ConditionParts.None"/>.</param>
    internal void Explain(string operation, string tableName, string? versionAttributeName, ConditionParts parts)
    {
        _operation = operation;
        _tableName = tableName;
        _versionAttributeName = versionAttributeName;
        _parts = parts;
    }
}
