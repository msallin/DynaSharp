using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>GetItem</c>.</summary>
internal readonly struct GetItemOperation : IOperation<GetItemRequest, GetItemResponse>
{
    public static string Name => "GetItem";

    public static string GetTableName(GetItemRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, GetItemRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        OperationJson.WriteItem(writer, "Key"u8, request.Key);
        OperationJson.WriteIfTrue(json, "ConsistentRead"u8, request.ConsistentRead);
        OperationJson.WriteStringIfSet(json, "ProjectionExpression"u8, request.ProjectionExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, request.ExpressionAttributeNames);
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        json.WriteEndObject();
    }

    public static GetItemResponse ReadResponse(ref DynamoDbReader reader)
    {
        IReadOnlyDictionary<string, AttributeValue>? item = null;
        ConsumedCapacity? consumedCapacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Item"u8))
            {
                item = AttributeValueConverter.ReadItem(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                consumedCapacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new GetItemResponse { Item = item, ConsumedCapacity = consumedCapacity };
    }

    public static void SetActivityTags(Activity activity, GetItemRequest request) =>
        ReadTags.SetProjection(activity, request.ConsistentRead, request.ProjectionExpression);

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(GetItemResponse response) => ConsumedCapacityTelemetry.ToList(response.ConsumedCapacity);
}
