using System.Collections.Frozen;
using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>BatchWriteItem</c>.</summary>
internal readonly struct BatchWriteItemOperation : IOperation<BatchWriteItemRequest, BatchWriteItemResponse>
{
    public static string Name => "BatchWriteItem";

    public static string? GetTableName(BatchWriteItemRequest request)
    {
        var scan = default(TableScan);
        scan.AddKeys(request.RequestItems);
        return scan.Only;
    }

    public static void WriteRequest(DynamoDbWriter writer, BatchWriteItemRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteStartObject("RequestItems"u8);
        foreach (var (table, writes) in request.RequestItems)
        {
            json.WriteStartArray(table);
            for (var i = 0; i < writes.Count; i++)
            {
                BatchJson.WriteWriteRequest(writer, writes[i]);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        OperationJson.WriteReturnItemCollectionMetrics(json, request.ReturnItemCollectionMetrics);
        json.WriteEndObject();
    }

    public static BatchWriteItemResponse ReadResponse(ref DynamoDbReader reader)
    {
        var unprocessedItems = default(UnprocessedItems);
        var envelope = BatchWriteEnvelope.Read(ref reader, ref unprocessedItems);
        return new BatchWriteItemResponse
        {
            UnprocessedItems = unprocessedItems.Value ?? (IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>>)FrozenDictionary<string, IReadOnlyList<WriteRequest>>.Empty,
            ItemCollectionMetrics = envelope.ItemCollectionMetrics ?? (IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>>)FrozenDictionary<string, IReadOnlyList<ItemCollectionMetrics>>.Empty,
            ConsumedCapacity = envelope.ConsumedCapacity,
        };
    }

    public static void SetActivityTags(Activity activity, BatchWriteItemRequest request)
    {
        var scan = TableScan.Collecting();
        scan.AddKeys(request.RequestItems);
        scan.SetTag(activity);
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(BatchWriteItemResponse response) => response.ConsumedCapacity;

    // "UnprocessedItems":{"Shop":[{"PutRequest":{...}}]}, parsed into requests by table.
    private struct UnprocessedItems : IResponseMember
    {
        public IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>>? Value { get; private set; }

        public void Read(ref DynamoDbReader reader) => Value = BatchJson.ReadWritesByTable(ref reader);
    }
}
