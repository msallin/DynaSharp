using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The members that the responses of <c>Query</c> and <c>Scan</c> share, read in one place so the two operations do not parse
/// the same envelope twice.
/// </summary>
/// <param name="Items">The items of the page.</param>
/// <param name="Count">The number of items in the page.</param>
/// <param name="ScannedCount">The number of items evaluated before filtering.</param>
/// <param name="LastEvaluatedKey">The key to continue with, or <see langword="null"/> on the last page.</param>
/// <param name="ConsumedCapacity">The consumed capacity, if requested.</param>
internal readonly record struct ItemPage(
    IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> Items,
    int Count,
    int ScannedCount,
    IReadOnlyDictionary<string, AttributeValue>? LastEvaluatedKey,
    ConsumedCapacity? ConsumedCapacity)
{
    /// <summary>Reads the response object of a <c>Query</c> or <c>Scan</c>; properties that are not known are skipped.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <returns>The page.</returns>
    public static ItemPage Read(ref DynamoDbReader reader)
    {
        var items = default(ItemList);
        var lastEvaluatedKey = default(LastKey);
        var envelope = PageEnvelope.Read(ref reader, ref items, ref lastEvaluatedKey);
        return new ItemPage(items.Value ?? [], envelope.Count, envelope.ScannedCount, lastEvaluatedKey.Value, envelope.ConsumedCapacity);
    }

    // "Items":[{...},{...}], parsed into dictionaries.
    private struct ItemList : IResponseMember
    {
        public IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>? Value { get; private set; }

        public void Read(ref DynamoDbReader reader) => Value = OperationJson.ReadItems(ref reader);
    }

    // "LastEvaluatedKey":{...}, parsed into a dictionary.
    private struct LastKey : IResponseMember
    {
        public IReadOnlyDictionary<string, AttributeValue>? Value { get; private set; }

        public void Read(ref DynamoDbReader reader) => Value = OperationJson.ReadLastEvaluatedKey(ref reader);
    }
}
