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
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>> responses =
            FrozenDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>>.Empty;
        IReadOnlyDictionary<string, KeysAndAttributes> unprocessedKeys = FrozenDictionary<string, KeysAndAttributes>.Empty;
        IReadOnlyList<ConsumedCapacity> consumedCapacity = [];
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Responses"u8))
            {
                responses = BatchJson.ReadItemsByTable(ref reader);
            }
            else if (reader.AttributeNameEquals("UnprocessedKeys"u8))
            {
                unprocessedKeys = BatchJson.ReadKeysByTable(ref reader);
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

        return new BatchGetItemResponse { Responses = responses, UnprocessedKeys = unprocessedKeys, ConsumedCapacity = consumedCapacity };
    }

    public static void SetActivityTags(Activity activity, BatchGetItemRequest request)
    {
        var scan = TableScan.Collecting();
        scan.AddKeys(request.RequestItems);
        scan.SetTag(activity);
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(BatchGetItemResponse response) => response.ConsumedCapacity;
}
