using System.Net;
using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// A batch get ended with keys left that DynamoDB did not answer. <see cref="Items"/> holds what was read and <see cref="UnprocessedKeys"/> the rest.
/// </summary>
/// <remarks>
/// <para>
/// This happens when a table stays throttled: requests in a row that get nothing done, or a request that fails with a throttling error on its last
/// attempt. It also happens when any other failure ends the batch after a request went through, because the caller would otherwise lose what was read. In those two cases the failure, as it
/// came, is the <see cref="Exception.InnerException"/>. A failure of the first request surfaces as it is.
/// </para>
/// <para>
/// A caller who cancels gets an <see cref="OperationCanceledException"/> and no report of the progress.
/// </para>
/// <para>
/// To go on, hand the rest to <c>client.BatchGetItemAsync</c> of <c>DynaSharp.Requests</c>:
/// <c>new BatchGetItemRequest { RequestItems = e.UnprocessedKeys }</c>. That API does not cut a batch into requests, so one that holds more than 100
/// keys must be split first. The keys are data of your application, so do not log them.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed class BatchGetIncompleteException<T> : DynamoDbException
{
    /// <summary>Initializes a new instance of the <see cref="BatchGetIncompleteException{T}"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="unprocessedKeys">The keys that were not answered, by table.</param>
    /// <param name="items">The items that were read.</param>
    /// <param name="innerException">The failure that ended the batch, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unprocessedKeys"/> or <paramref name="items"/> is <see langword="null"/>.</exception>
    public BatchGetIncompleteException(string message, IReadOnlyDictionary<string, KeysAndAttributes> unprocessedKeys, IReadOnlyList<T> items, Exception? innerException = null)
        : base(message, BatchIncompleteException.Code, HttpStatusCode.OK, null, innerException)
    {
        ArgumentNullException.ThrowIfNull(unprocessedKeys);
        ArgumentNullException.ThrowIfNull(items);
        UnprocessedKeys = unprocessedKeys;
        Items = items;
    }

    /// <summary>Gets the keys that were not answered, by table, in the form <c>client.BatchGetItemAsync</c> takes, with the consistency and the projection of the batch. It includes the requests the batch did not get to.</summary>
    public IReadOnlyDictionary<string, KeysAndAttributes> UnprocessedKeys { get; }

    /// <summary>Gets the items that were read before the batch gave up.</summary>
    public IReadOnlyList<T> Items { get; }
}
