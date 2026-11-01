using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>CreateTable</c>.</summary>
/// <remarks>
/// Empty optional lists are omitted from the request. A required enum that is not set (<c>Unknown</c>) or not defined throws
/// <see cref="ArgumentOutOfRangeException"/> while the request is serialized; an optional one that is <c>Unspecified</c> is omitted.
/// </remarks>
internal readonly struct CreateTableOperation : IOperation<CreateTableRequest, CreateTableResponse>
{
    public static string Name => "CreateTable";

    public static string GetTableName(CreateTableRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, CreateTableRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        TableJson.WriteAttributeDefinitions(json, request.AttributeDefinitions);
        json.WriteString("TableName"u8, request.TableName);
        TableJson.WriteKeySchema(json, request.KeySchema);
        if (request.LocalSecondaryIndexes is { Count: > 0 } localIndexes)
        {
            json.WriteStartArray("LocalSecondaryIndexes"u8);
            for (var i = 0; i < localIndexes.Count; i++)
            {
                var index = localIndexes[i];
                json.WriteStartObject();
                json.WriteString("IndexName"u8, index.IndexName);
                TableJson.WriteKeySchema(json, index.KeySchema);
                TableJson.WriteProjection(json, index.Projection);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        if (request.GlobalSecondaryIndexes is { Count: > 0 } globalIndexes)
        {
            json.WriteStartArray("GlobalSecondaryIndexes"u8);
            for (var i = 0; i < globalIndexes.Count; i++)
            {
                json.WriteStartObject();
                TableJson.WriteGlobalSecondaryIndexMembers(json, globalIndexes[i]);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        WireNames.BillingModes.WriteIfSet(json, "BillingMode"u8, request.BillingMode);
        TableJson.WriteProvisionedThroughput(json, request.ProvisionedThroughput);
        TableJson.WriteStreamSpecification(json, request.StreamSpecification);
        WriteSseSpecification(json, request.SSESpecification);
        WriteTags(json, request.Tags);
        WireNames.TableClasses.WriteIfSet(json, "TableClass"u8, request.TableClass);
        OperationJson.WriteIfTrue(json, "DeletionProtectionEnabled"u8, request.DeletionProtectionEnabled);
        json.WriteEndObject();
    }

    public static CreateTableResponse ReadResponse(ref DynamoDbReader reader) =>
        new() { TableDescription = TableJson.ReadTableDescriptionEnvelope(ref reader, Name) };

    public static void SetActivityTags(Activity activity, CreateTableRequest request) =>
        OperationTelemetry.RecordProvisionedThroughput(activity, request.ProvisionedThroughput);

    private static void WriteSseSpecification(Utf8JsonWriter json, SSESpecification? specification)
    {
        if (specification is null)
        {
            return;
        }

        json.WriteStartObject("SSESpecification"u8);
        OperationJson.WriteIfTrue(json, "Enabled"u8, specification.Enabled);
        WireNames.SSETypes.WriteIfSet(json, "SSEType"u8, specification.SSEType);
        OperationJson.WriteStringIfSet(json, "KMSMasterKeyId"u8, specification.KMSMasterKeyId);
        json.WriteEndObject();
    }

    private static void WriteTags(Utf8JsonWriter json, IReadOnlyList<TableTag>? tags)
    {
        if (tags is not { Count: > 0 })
        {
            return;
        }

        json.WriteStartArray("Tags"u8);
        for (var i = 0; i < tags.Count; i++)
        {
            json.WriteStartObject();
            json.WriteString("Key"u8, tags[i].Key);
            json.WriteString("Value"u8, tags[i].Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}
