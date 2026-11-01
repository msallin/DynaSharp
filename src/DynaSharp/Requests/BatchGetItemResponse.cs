using System.Collections.Frozen;

namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="BatchGetItemRequest"/>: the items that were read and the keys that were not.
/// </summary>
/// <remarks>
/// A successful call can be a partial result. DynamoDB stops at 16 MB, at 1 MB per partition, when a table is throttled or when
/// it fails internally for some of the keys, and returns the keys it did not read in <see cref="UnprocessedKeys"/>. The caller
/// must send those keys again (<see cref="BatchGetItemRequest.RequestItems"/> accepts the map as it is), preferably after a
/// growing wait. A call fails as a whole only when none of the keys could be processed.
/// </remarks>
public sealed record BatchGetItemResponse
{
    /// <summary>
    /// Gets the items that were read, by table name (or table ARN) as in the request. Items come in no particular order. A
    /// requested key without an item contributes nothing, and a table without any item found can be missing; the map is empty when
    /// nothing was found. Never <see langword="null"/>. The collections are read-only; do not cast them to change them.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>> Responses { get; init; } =
        FrozenDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>>.Empty;

    /// <summary>
    /// Gets the keys that were not read, in the form of the request, so that they can be sent as
    /// <see cref="BatchGetItemRequest.RequestItems"/>. Empty when everything was read; never <see langword="null"/>.
    /// </summary>
    public IReadOnlyDictionary<string, KeysAndAttributes> UnprocessedKeys { get; init; } = FrozenDictionary<string, KeysAndAttributes>.Empty;

    /// <summary>Gets the read capacity consumed, one entry per table, when the request asked for it. Empty otherwise; never <see langword="null"/>.</summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; init; } = [];
}
