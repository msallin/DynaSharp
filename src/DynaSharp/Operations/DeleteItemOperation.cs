using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>DeleteItem</c>.</summary>
internal readonly struct DeleteItemOperation : IOperation<DeleteItemRequest, DeleteItemResponse>
{
    public static string Name => "DeleteItem";

    public static string GetTableName(DeleteItemRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, DeleteItemRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        OperationJson.WriteItem(writer, "Key"u8, request.Key);
        OperationJson.WriteStringIfSet(json, "ConditionExpression"u8, request.ConditionExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, request.ExpressionAttributeNames);
        OperationJson.WriteAttributeValueMap(writer, "ExpressionAttributeValues"u8, request.ExpressionAttributeValues);
        OperationJson.WriteReturnValues(json, request.ReturnValues);
        OperationJson.WriteReturnValuesOnConditionCheckFailure(json, request.ReturnValuesOnConditionCheckFailure);
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        OperationJson.WriteReturnItemCollectionMetrics(json, request.ReturnItemCollectionMetrics);
        json.WriteEndObject();
    }

    public static DeleteItemResponse ReadResponse(ref DynamoDbReader reader)
    {
        var result = ItemWriteResult.Read(ref reader);
        return new DeleteItemResponse { Attributes = result.Attributes, ConsumedCapacity = result.ConsumedCapacity, ItemCollectionMetrics = result.ItemCollectionMetrics };
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(DeleteItemResponse response) => ConsumedCapacityTelemetry.ToList(response.ConsumedCapacity);

    public static Item? GetKey(DeleteItemRequest request) => new(request.Key);
}
