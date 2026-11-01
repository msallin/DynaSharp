using System.Diagnostics;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>ListTables</c>.</summary>
internal readonly struct ListTablesOperation : IOperation<ListTablesRequest, ListTablesResponse>
{
    public static string Name => "ListTables";

    // The call lists tables, it does not address one.
    public static string? GetTableName(ListTablesRequest request) => null;

    public static void WriteRequest(DynamoDbWriter writer, ListTablesRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        OperationJson.WriteStringIfSet(json, "ExclusiveStartTableName"u8, request.ExclusiveStartTableName);
        OperationJson.WriteInt32IfSet(json, "Limit"u8, request.Limit);
        json.WriteEndObject();
    }

    public static ListTablesResponse ReadResponse(ref DynamoDbReader reader)
    {
        IReadOnlyList<string> tableNames = [];
        string? lastEvaluatedTableName = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TableNames"u8))
            {
                tableNames = OperationJson.ReadStringArray(ref reader);
            }
            else if (reader.AttributeNameEquals("LastEvaluatedTableName"u8))
            {
                lastEvaluatedTableName = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new ListTablesResponse { TableNames = tableNames, LastEvaluatedTableName = lastEvaluatedTableName };
    }

    public static void SetActivityTags(Activity activity, ListTablesRequest request)
    {
        if (request.ExclusiveStartTableName is { } exclusiveStartTableName)
        {
            activity.SetTag(TelemetryNames.ExclusiveStartTable, exclusiveStartTableName);
        }

        ReadTags.SetLimit(activity, request.Limit);
    }

    public static void SetResponseTags(Activity activity, ListTablesResponse response) =>
        activity.SetTag(TelemetryNames.TableCount, response.TableNames.Count);
}
