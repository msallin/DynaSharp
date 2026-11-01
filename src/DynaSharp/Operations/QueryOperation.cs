using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>Query</c>.</summary>
internal readonly struct QueryOperation : IOperation<QueryRequest, QueryResponse>
{
    public static string Name => "Query";

    public static string GetTableName(QueryRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, QueryRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        OperationJson.WriteStringIfSet(json, "IndexName"u8, request.IndexName);
        OperationJson.WriteSelect(json, request.Select);
        OperationJson.WriteInt32IfSet(json, "Limit"u8, request.Limit);
        OperationJson.WriteIfTrue(json, "ConsistentRead"u8, request.ConsistentRead);
        OperationJson.WriteBooleanIfSet(json, "ScanIndexForward"u8, request.ScanIndexForward);
        OperationJson.WriteItemIfSet(writer, "ExclusiveStartKey"u8, request.ExclusiveStartKey);
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        OperationJson.WriteStringIfSet(json, "ProjectionExpression"u8, request.ProjectionExpression);
        OperationJson.WriteStringIfSet(json, "FilterExpression"u8, request.FilterExpression);
        json.WriteString("KeyConditionExpression"u8, request.KeyConditionExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, request.ExpressionAttributeNames);
        OperationJson.WriteAttributeValueMap(writer, "ExpressionAttributeValues"u8, request.ExpressionAttributeValues);
        json.WriteEndObject();
    }

    public static QueryResponse ReadResponse(ref DynamoDbReader reader)
    {
        var page = ItemPage.Read(ref reader);
        return new QueryResponse
        {
            Items = page.Items,
            Count = page.Count,
            ScannedCount = page.ScannedCount,
            LastEvaluatedKey = page.LastEvaluatedKey,
            ConsumedCapacity = page.ConsumedCapacity,
        };
    }

    public static void SetActivityTags(Activity activity, QueryRequest request)
    {
        ReadTags.Set(activity, request.ConsistentRead, request.ProjectionExpression, request.IndexName, request.Limit, request.Select);
        if (request.ScanIndexForward is { } scanForward)
        {
            activity.SetTag(TelemetryNames.ScanForward, scanForward);
        }
    }

    public static void SetResponseTags(Activity activity, QueryResponse response) => ReadTags.SetResponse(activity, response.Count, response.ScannedCount);

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(QueryResponse response) => ConsumedCapacityTelemetry.ToList(response.ConsumedCapacity);
}
