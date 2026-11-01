using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>UpdateTable</c>.</summary>
/// <remarks>
/// What is not set is not sent: empty optional lists, enums that are <c>Unspecified</c> and a deletion protection of <see langword="null"/>.
/// A change that is set but not defined (an enum value outside its range) throws <see cref="ArgumentOutOfRangeException"/> while the
/// request is serialized.
/// </remarks>
internal readonly struct UpdateTableOperation : IOperation<UpdateTableRequest, UpdateTableResponse>
{
    public static string Name => "UpdateTable";

    public static string GetTableName(UpdateTableRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, UpdateTableRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        if (request.AttributeDefinitions is { Count: > 0 } definitions)
        {
            TableJson.WriteAttributeDefinitions(json, definitions);
        }

        json.WriteString("TableName"u8, request.TableName);
        WireNames.BillingModes.WriteIfSet(json, "BillingMode"u8, request.BillingMode);
        TableJson.WriteProvisionedThroughput(json, request.ProvisionedThroughput);
        WriteIndexUpdates(json, request.GlobalSecondaryIndexUpdates);
        TableJson.WriteStreamSpecification(json, request.StreamSpecification);
        WireNames.TableClasses.WriteIfSet(json, "TableClass"u8, request.TableClass);
        OperationJson.WriteBooleanIfSet(json, "DeletionProtectionEnabled"u8, request.DeletionProtectionEnabled);
        json.WriteEndObject();
    }

    public static UpdateTableResponse ReadResponse(ref DynamoDbReader reader) =>
        new() { TableDescription = TableJson.ReadTableDescriptionEnvelope(ref reader) };

    public static void SetActivityTags(Activity activity, UpdateTableRequest request) =>
        OperationTelemetry.RecordProvisionedThroughput(activity, request.ProvisionedThroughput);

    private static void WriteIndexUpdates(Utf8JsonWriter json, IReadOnlyList<GlobalSecondaryIndexUpdate>? updates)
    {
        if (updates is not { Count: > 0 })
        {
            return;
        }

        json.WriteStartArray("GlobalSecondaryIndexUpdates"u8);
        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];
            json.WriteStartObject();
            if (update.Create is { } create)
            {
                json.WriteStartObject("Create"u8);
                TableJson.WriteGlobalSecondaryIndexMembers(json, create);
                json.WriteEndObject();
            }

            if (update.Update is { } change)
            {
                json.WriteStartObject("Update"u8);
                json.WriteString("IndexName"u8, change.IndexName);
                TableJson.WriteProvisionedThroughput(json, change.ProvisionedThroughput);
                json.WriteEndObject();
            }

            if (update.Delete is { } delete)
            {
                json.WriteStartObject("Delete"u8);
                json.WriteString("IndexName"u8, delete.IndexName);
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}
