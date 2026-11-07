using System.Net;
using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// A batch write ended with writes left. A batch is not atomic, so the writes that were taken stay applied: <see cref="Written"/> says how many and
/// <see cref="UnprocessedItems"/> holds the rest.
/// </summary>
/// <remarks>
/// <para>
/// This happens when a table stays throttled: requests in a row that get nothing done, or a request that fails with a throttling error on its last
/// attempt. It also happens when any other failure ends the batch after a request went through, because the caller would otherwise lose what was done, and when the first request
/// fails with a timeout, a lost connection or a server error after its last attempt. In those cases the failure, as it came, is the <see cref="Exception.InnerException"/>.
/// DynamoDB may have applied part of a request that got no answer, so its writes count as unprocessed, and sending them again is safe. Any other failure of the first request,
/// such as a <see cref="ValidationException"/>, applied nothing and surfaces as it is.
/// </para>
/// <para>
/// A caller who cancels gets an <see cref="OperationCanceledException"/> and no report of the progress, whatever was applied by then.
/// </para>
/// <para>
/// To go on, hand the rest to <c>client.BatchWriteItemAsync</c> of <c>DynaSharp.Requests</c>:
/// <c>new BatchWriteItemRequest { RequestItems = e.UnprocessedItems }</c>. That API does not cut a batch into requests, so one that holds more
/// than 25 writes must be split first. The writes hold the items, so do not log them.
/// </para>
/// </remarks>
public sealed class BatchIncompleteException : DynamoDbException
{
    /// <summary>The <see cref="DynamoDbException.ErrorCode"/> of the exception, which the client gives it: it is no code of the service. What the service said, if anything, is in the <see cref="Exception.InnerException"/>.</summary>
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

    /// <summary>
    /// Gets the writes that were not applied, by table, in the form <c>client.BatchWriteItemAsync</c> takes. It includes the requests the batch did not get to, and all of a request that got no
    /// answer: a timeout says nothing about what DynamoDB did, so some of those can be applied. Sending a put or a delete again is safe.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>> UnprocessedItems { get; }

    /// <summary>Gets how many writes of the batch DynamoDB confirmed.</summary>
    public int Written { get; }
}
