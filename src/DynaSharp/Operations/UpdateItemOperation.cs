using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>UpdateItem</c>.</summary>
internal readonly struct UpdateItemOperation : IOperation<UpdateItemRequest, UpdateItemResponse>
{
    public static string Name => "UpdateItem";

    public static string GetTableName(UpdateItemRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, UpdateItemRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        OperationJson.WriteItem(writer, "Key"u8, request.Key);
        OperationJson.WriteStringIfSet(json, "UpdateExpression"u8, request.UpdateExpression);
        OperationJson.WriteStringIfSet(json, "ConditionExpression"u8, request.ConditionExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, request.ExpressionAttributeNames);
        OperationJson.WriteAttributeValueMap(writer, "ExpressionAttributeValues"u8, request.ExpressionAttributeValues);
        OperationJson.WriteReturnValues(json, request.ReturnValues);
        OperationJson.WriteReturnValuesOnConditionCheckFailure(json, request.ReturnValuesOnConditionCheckFailure);
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        OperationJson.WriteReturnItemCollectionMetrics(json, request.ReturnItemCollectionMetrics);
        json.WriteEndObject();
    }

    public static UpdateItemResponse ReadResponse(ref DynamoDbReader reader)
    {
        var result = ItemWriteResult.Read(ref reader);
        return new UpdateItemResponse { Attributes = result.Attributes, ConsumedCapacity = result.ConsumedCapacity, ItemCollectionMetrics = result.ItemCollectionMetrics };
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(UpdateItemResponse response) => ConsumedCapacityTelemetry.ToList(response.ConsumedCapacity);

    public static Item? GetKey(UpdateItemRequest request) => new(request.Key);
}
