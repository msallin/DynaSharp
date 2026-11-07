using System.Collections.Frozen;
using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>BatchGetItem</c>.</summary>
internal readonly struct BatchGetItemOperation : IOperation<BatchGetItemRequest, BatchGetItemResponse>
{
    public static string Name => "BatchGetItem";

    public static string? GetTableName(BatchGetItemRequest request)
    {
        var scan = default(TableScan);
        scan.AddKeys(request.RequestItems);
        return scan.Only;
    }

    public static void WriteRequest(DynamoDbWriter writer, BatchGetItemRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteStartObject("RequestItems"u8);
        foreach (var (table, keysAndAttributes) in request.RequestItems)
        {
            json.WritePropertyName(table);
            BatchJson.WriteKeysAndAttributes(writer, keysAndAttributes);
        }

        json.WriteEndObject();
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        json.WriteEndObject();
    }

    public static BatchGetItemResponse ReadResponse(ref DynamoDbReader reader)
    {
        var responses = default(Responses);
        var unprocessedKeys = default(UnprocessedKeys);
        var envelope = BatchGetEnvelope.Read(ref reader, ref responses, ref unprocessedKeys);
        return new BatchGetItemResponse
        {
            Responses = responses.Value ?? (IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>>)FrozenDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>>.Empty,
            UnprocessedKeys = unprocessedKeys.Value ?? (IReadOnlyDictionary<string, KeysAndAttributes>)FrozenDictionary<string, KeysAndAttributes>.Empty,
            ConsumedCapacity = envelope.ConsumedCapacity,
        };
    }

    public static void SetActivityTags(Activity activity, BatchGetItemRequest request)
    {
        var scan = TableScan.Collecting();
        scan.AddKeys(request.RequestItems);
        scan.SetTag(activity);
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(BatchGetItemResponse response) => response.ConsumedCapacity;

    // "Responses":{"Shop":[{...}]}, parsed into items by table.
    private struct Responses : IResponseMember
    {
        public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>>? Value { get; private set; }

        public void Read(ref DynamoDbReader reader) => Value = BatchJson.ReadItemsByTable(ref reader);
    }

    // "UnprocessedKeys":{"Shop":{"Keys":[{...}]}}, parsed into keys by table.
    private struct UnprocessedKeys : IResponseMember
    {
        public IReadOnlyDictionary<string, KeysAndAttributes>? Value { get; private set; }

        public void Read(ref DynamoDbReader reader) => Value = BatchJson.ReadKeysByTable(ref reader);
    }
}
