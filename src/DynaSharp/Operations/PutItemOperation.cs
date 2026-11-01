using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>PutItem</c>.</summary>
internal readonly struct PutItemOperation : IOperation<PutItemRequest, PutItemResponse>
{
    public static string Name => "PutItem";

    public static string GetTableName(PutItemRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, PutItemRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        OperationJson.WriteItem(writer, "Item"u8, request.Item);
        OperationJson.WriteStringIfSet(json, "ConditionExpression"u8, request.ConditionExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, request.ExpressionAttributeNames);
        OperationJson.WriteAttributeValueMap(writer, "ExpressionAttributeValues"u8, request.ExpressionAttributeValues);
        OperationJson.WriteReturnValues(json, request.ReturnValues);
        OperationJson.WriteReturnValuesOnConditionCheckFailure(json, request.ReturnValuesOnConditionCheckFailure);
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        OperationJson.WriteReturnItemCollectionMetrics(json, request.ReturnItemCollectionMetrics);
        json.WriteEndObject();
    }

    public static PutItemResponse ReadResponse(ref DynamoDbReader reader)
    {
        var result = ItemWriteResult.Read(ref reader);
        return new PutItemResponse { Attributes = result.Attributes, ConsumedCapacity = result.ConsumedCapacity, ItemCollectionMetrics = result.ItemCollectionMetrics };
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(PutItemResponse response) => ConsumedCapacityTelemetry.ToList(response.ConsumedCapacity);
}
