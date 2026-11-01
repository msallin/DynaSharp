using System.Net;
using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// A batch write ended with writes left that DynamoDB did not take, after it sent them again as often as it could. A batch is not atomic, so the
/// writes that were taken stay applied: <see cref="Written"/> says how many and <see cref="UnprocessedItems"/> holds the rest.
/// </summary>
/// <remarks>
/// <para>
/// This happens when a table stays throttled: requests in a row that get nothing done, or a request that fails with a throttling error on its last
/// attempt, which is then the <see cref="Exception.InnerException"/>. Any other failure surfaces as it is, with the requests before it applied.
/// </para>
/// <para>
/// To go on, hand the rest to <c>client.BatchWriteItemAsync</c> of <c>DynaSharp.Requests</c>:
/// <c>new BatchWriteItemRequest { RequestItems = e.UnprocessedItems }</c>. That API does not cut a batch into requests, so one that holds more
/// than 25 writes must be split first. The writes hold the items, so do not log them.
/// </para>
/// </remarks>
public sealed class BatchIncompleteException : DynamoDbException
{
    /// <summary>The <see cref="DynamoDbException.ErrorCode"/> of the exception, which the client gives it: DynamoDB answered every request.</summary>
    public const string Code = "BatchIncomplete";

    /// <summary>Initializes a new instance of the <see cref="BatchIncompleteException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="unprocessedItems">The writes that were not applied, by table.</param>
    /// <param name="written">How many writes were applied.</param>
    /// <param name="innerException">The failure that ended the batch, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="unprocessedItems"/> is <see langword="null"/>.</exception>
    public BatchIncompleteException(string message, IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>> unprocessedItems, int written, Exception? innerException = null)
        : base(message, Code, HttpStatusCode.OK, null, innerException)
    {
        ArgumentNullException.ThrowIfNull(unprocessedItems);
        UnprocessedItems = unprocessedItems;
        Written = written;
    }

    /// <summary>Gets the writes that were not applied, by table, in the form <c>client.BatchWriteItemAsync</c> takes. It includes the requests the batch did not get to.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>> UnprocessedItems { get; }

    /// <summary>Gets how many writes of the batch were applied.</summary>
    public int Written { get; }
}
