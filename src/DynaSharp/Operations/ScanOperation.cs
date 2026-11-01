using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>Scan</c>.</summary>
internal readonly struct ScanOperation : IOperation<ScanRequest, ScanResponse>
{
    public static string Name => "Scan";

    public static string GetTableName(ScanRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, ScanRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        OperationJson.WriteStringIfSet(json, "IndexName"u8, request.IndexName);
        OperationJson.WriteSelect(json, request.Select);
        OperationJson.WriteInt32IfSet(json, "Limit"u8, request.Limit);
        OperationJson.WriteIfTrue(json, "ConsistentRead"u8, request.ConsistentRead);
        OperationJson.WriteItemIfSet(writer, "ExclusiveStartKey"u8, request.ExclusiveStartKey);
        OperationJson.WriteInt32IfSet(json, "TotalSegments"u8, request.TotalSegments);
        OperationJson.WriteInt32IfSet(json, "Segment"u8, request.Segment);
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        OperationJson.WriteStringIfSet(json, "ProjectionExpression"u8, request.ProjectionExpression);
        OperationJson.WriteStringIfSet(json, "FilterExpression"u8, request.FilterExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, request.ExpressionAttributeNames);
        OperationJson.WriteAttributeValueMap(writer, "ExpressionAttributeValues"u8, request.ExpressionAttributeValues);
        json.WriteEndObject();
    }

    public static ScanResponse ReadResponse(ref DynamoDbReader reader)
    {
        var page = ItemPage.Read(ref reader);
        return new ScanResponse
        {
            Items = page.Items,
            Count = page.Count,
            ScannedCount = page.ScannedCount,
            LastEvaluatedKey = page.LastEvaluatedKey,
            ConsumedCapacity = page.ConsumedCapacity,
        };
    }

    public static void SetActivityTags(Activity activity, ScanRequest request)
    {
        ReadTags.Set(activity, request.ConsistentRead, request.ProjectionExpression, request.IndexName, request.Limit, request.Select);
        if (request.Segment is { } segment)
        {
            activity.SetTag(TelemetryNames.Segment, segment);
        }

        if (request.TotalSegments is { } totalSegments)
        {
            activity.SetTag(TelemetryNames.TotalSegments, totalSegments);
        }
    }

    public static void SetResponseTags(Activity activity, ScanResponse response) => ReadTags.SetResponse(activity, response.Count, response.ScannedCount);

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(ScanResponse response) => ConsumedCapacityTelemetry.ToList(response.ConsumedCapacity);
}
