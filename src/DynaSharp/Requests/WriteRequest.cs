namespace DynaSharp.Requests;

/// <summary>
/// One write of a <see cref="BatchWriteItemRequest"/>: either a put or a delete. DynamoDB returns the writes it did not process in
/// the same form as <see cref="BatchWriteItemResponse.UnprocessedItems"/>.
/// </summary>
/// <remarks>
/// Set exactly one of <see cref="PutRequest"/> and <see cref="DeleteRequest"/>. The library sends whatever is set and leaves the
/// check to DynamoDB, which rejects a write with neither or with both with a <see cref="ValidationException"/>. A write has no
/// condition expression; use <see cref="TransactWriteItemsRequest"/> when a write must be conditional.
/// </remarks>
public sealed record WriteRequest
{
    /// <summary>Gets the put, or <see langword="null"/> if this write is a delete.</summary>
    public PutRequest? PutRequest { get; init; }

    /// <summary>Gets the delete, or <see langword="null"/> if this write is a put.</summary>
    public DeleteRequest? DeleteRequest { get; init; }
}
