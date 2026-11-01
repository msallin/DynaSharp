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
        IReadOnlyDictionary<string, IReadOnlyList<WriteRequest>> unprocessedItems = FrozenDictionary<string, IReadOnlyList<WriteRequest>>.Empty;
        IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> itemCollectionMetrics = FrozenDictionary<string, IReadOnlyList<ItemCollectionMetrics>>.Empty;
        IReadOnlyList<ConsumedCapacity> consumedCapacity = [];
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("UnprocessedItems"u8))
            {
                unprocessedItems = BatchJson.ReadWritesByTable(ref reader);
            }
            else if (reader.AttributeNameEquals("ItemCollectionMetrics"u8))
            {
                itemCollectionMetrics = OperationJson.ReadItemCollectionMetricsByTable(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                consumedCapacity = OperationJson.ReadConsumedCapacityList(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new BatchWriteItemResponse { UnprocessedItems = unprocessedItems, ItemCollectionMetrics = itemCollectionMetrics, ConsumedCapacity = consumedCapacity };
    }

    public static void SetActivityTags(Activity activity, BatchWriteItemRequest request)
    {
        var scan = TableScan.Collecting();
        scan.AddKeys(request.RequestItems);
        scan.SetTag(activity);
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(BatchWriteItemResponse response) => response.ConsumedCapacity;
}
