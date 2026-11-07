using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>TransactGetItems</c>.</summary>
internal readonly struct TransactGetItemsOperation : IOperation<TransactGetItemsRequest, TransactGetItemsResponse>
{
    public static string Name => "TransactGetItems";

    public static string? GetTableName(TransactGetItemsRequest request)
    {
        var scan = default(TableScan);
        Scan(ref scan, request);
        return scan.Only;
    }

    public static void WriteRequest(DynamoDbWriter writer, TransactGetItemsRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteStartArray("TransactItems"u8);
        var items = request.TransactItems;
        for (var i = 0; i < items.Count; i++)
        {
            var get = items[i].Get;
            json.WriteStartObject();
            json.WriteStartObject("Get"u8);
            json.WriteString("TableName"u8, get.TableName);
            OperationJson.WriteItem(writer, "Key"u8, get.Key);
            OperationJson.WriteStringIfSet(json, "ProjectionExpression"u8, get.ProjectionExpression);
            OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, get.ExpressionAttributeNames);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        json.WriteEndObject();
    }

    public static TransactGetItemsResponse ReadResponse(ref DynamoDbReader reader)
    {
        var responses = default(ItemResponses);
        var envelope = TransactGetEnvelope.Read(ref reader, ref responses);
        return new TransactGetItemsResponse { Responses = responses.Value ?? [], ConsumedCapacity = envelope.ConsumedCapacity };
    }

    public static void SetActivityTags(Activity activity, TransactGetItemsRequest request)
    {
        var scan = TableScan.Collecting();
        Scan(ref scan, request);
        scan.SetTag(activity);
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(TransactGetItemsResponse response) => response.ConsumedCapacity;

    private static void Scan(ref TableScan scan, TransactGetItemsRequest request)
    {
        var items = request.TransactItems;
        for (var i = 0; i < items.Count; i++)
        {
            scan.Add(items[i].Get.TableName);
        }
    }

    // "Responses":[{"Item":{...}},{}]: an entry per action. An item that does not exist is {}; the documentation also calls it null.
    private static ReadOnlyCollection<ItemResponse> ReadItemResponses(ref DynamoDbReader reader)
    {
        var responses = new List<ItemResponse>();
        OperationJson.ReadStartArray(ref reader);
        while (true)
        {
            reader.Advance();
            switch (reader.Json.TokenType)
            {
                case JsonTokenType.EndArray:
                    return responses.AsReadOnly();
                case JsonTokenType.Null:
                    responses.Add(new ItemResponse());
                    break;
                case JsonTokenType.StartObject:
                    responses.Add(ReadItemResponse(ref reader));
                    break;
                default:
                    throw new JsonException($"Expected an item response or the end of the array but found {reader.Json.TokenType} at byte {reader.Json.TokenStartIndex}.");
            }
        }
    }

    // The reader has consumed the opening brace.
    private static ItemResponse ReadItemResponse(ref DynamoDbReader reader)
    {
        IReadOnlyDictionary<string, AttributeValue>? item = null;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Item"u8))
            {
                item = AttributeValueConverter.ReadItem(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new ItemResponse { Item = item };
    }

    // "Responses":[{"Item":{...}},{}], parsed into an entry per action.
    private struct ItemResponses : IResponseMember
    {
        public IReadOnlyList<ItemResponse>? Value { get; private set; }

        public void Read(ref DynamoDbReader reader) => Value = ReadItemResponses(ref reader);
    }
}
