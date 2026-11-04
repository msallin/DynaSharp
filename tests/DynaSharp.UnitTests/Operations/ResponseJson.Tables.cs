using System.Text.Json;
using System.Text.Json.Nodes;
using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Operations;

// The table operations: CreateTable, DeleteTable, UpdateTable, ListTables and the Time to Live operations. The enums are written with the names DynamoDB uses, spelled
// out here and not taken from the library, so that the comparison with botocore checks the library's mapping.
internal static partial class ResponseJson
{
    public static string Describe(CreateTableResponse response) => Build(json => WriteTableEnvelope(json, response.TableDescription!));

    public static string Describe(DeleteTableResponse response) => Build(json => WriteTableEnvelope(json, response.TableDescription!));

    public static string Describe(UpdateTableResponse response) => Build(json => WriteTableEnvelope(json, response.TableDescription!));

    public static string Describe(ListTablesResponse response) => Build(json =>
    {
        json.WriteStartArray("TableNames");
        foreach (var name in response.TableNames)
        {
            json.WriteStringValue(name);
        }

        json.WriteEndArray();
        WriteString(json, "LastEvaluatedTableName", response.LastEvaluatedTableName);
    });

    public static string Describe(DescribeTimeToLiveResponse response) => Build(json =>
    {
        if (response.TimeToLiveDescription is { } description)
        {
            json.WriteStartObject("TimeToLiveDescription");
            WriteString(json, "TimeToLiveStatus", description.TimeToLiveStatus switch
            {
                TimeToLiveStatus.Enabling => "ENABLING",
                TimeToLiveStatus.Disabling => "DISABLING",
                TimeToLiveStatus.Enabled => "ENABLED",
                TimeToLiveStatus.Disabled => "DISABLED",
                _ => null,
            });
            WriteString(json, "AttributeName", description.AttributeName);
            json.WriteEndObject();
        }
    });

    public static string Describe(UpdateTimeToLiveResponse response) => Build(json =>
    {
        if (response.TimeToLiveSpecification is { } specification)
        {
            json.WriteStartObject("TimeToLiveSpecification");
            json.WriteString("AttributeName", specification.AttributeName);
            json.WriteBoolean("Enabled", specification.Enabled);
            json.WriteEndObject();
        }
    });

    /// <summary>
    /// Brings what botocore parsed to what the library reports: the library models a part of the table description (the members that
    /// it leaves out are removed here) and reports an absent list, count or flag with its default (added here).
    /// </summary>
    /// <param name="botocoreJson">What botocore parsed.</param>
    /// <returns>The same, reduced to the modelled members and with the defaults filled in.</returns>
    public static string WithTableDefaults(string botocoreJson)
    {
        var response = JsonNode.Parse(botocoreJson)!.AsObject();
        var table = response["TableDescription"]!.AsObject();
        Keep(table, "TableName", "TableArn", "TableStatus", "KeySchema", "AttributeDefinitions", "ItemCount", "TableSizeBytes", "CreationDateTime", "BillingModeSummary", "LocalSecondaryIndexes", "GlobalSecondaryIndexes", "StreamSpecification", "LatestStreamArn", "LatestStreamLabel", "DeletionProtectionEnabled", "ProvisionedThroughput", "TableClassSummary", "SSEDescription", "OnDemandThroughput");
        table.TryAdd("KeySchema", new JsonArray());
        table.TryAdd("AttributeDefinitions", new JsonArray());
        table.TryAdd("ItemCount", 0);
        table.TryAdd("TableSizeBytes", 0);
        table.TryAdd("LocalSecondaryIndexes", new JsonArray());
        table.TryAdd("GlobalSecondaryIndexes", new JsonArray());
        table.TryAdd("DeletionProtectionEnabled", false);
        KeepCapacity(table);
        if (table["TableClassSummary"] is JsonObject summary)
        {
            Keep(summary, "TableClass");
        }

        KeepOnDemand(table);
        if (table["SSEDescription"] is JsonObject encryption)
        {
            Keep(encryption, "Status", "SSEType", "KMSMasterKeyArn", "InaccessibleEncryptionDateTime");
        }

        foreach (var index in table["LocalSecondaryIndexes"]!.AsArray().Select(static node => node!.AsObject()))
        {
            Keep(index, "IndexName", "KeySchema", "Projection");
            index.TryAdd("KeySchema", new JsonArray());
        }

        foreach (var index in table["GlobalSecondaryIndexes"]!.AsArray().Select(static node => node!.AsObject()))
        {
            Keep(index, "IndexName", "IndexStatus", "Backfilling", "KeySchema", "Projection", "ProvisionedThroughput", "OnDemandThroughput");
            index.TryAdd("KeySchema", new JsonArray());
            index.TryAdd("Backfilling", false);
            KeepCapacity(index);
            KeepOnDemand(index);
        }

        return response.ToJsonString();
    }

    // The library reports the two capacity numbers of a ProvisionedThroughput and zero for one that is missing.
    private static void KeepCapacity(JsonObject node)
    {
        if (node["ProvisionedThroughput"] is JsonObject capacity)
        {
            Keep(capacity, "ReadCapacityUnits", "WriteCapacityUnits");
            capacity.TryAdd("ReadCapacityUnits", 0);
            capacity.TryAdd("WriteCapacityUnits", 0);
        }
    }

    private static void KeepOnDemand(JsonObject node)
    {
        if (node["OnDemandThroughput"] is JsonObject maximums)
        {
            Keep(maximums, "MaxReadRequestUnits", "MaxWriteRequestUnits");
        }
    }

    private static void Keep(JsonObject node, params string[] names)
    {
        foreach (var name in node.Select(static property => property.Key).Except(names).ToList())
        {
            node.Remove(name);
        }
    }

    private static void WriteTableEnvelope(Utf8JsonWriter json, TableDescription table)
    {
        json.WriteStartObject("TableDescription");
        WriteString(json, "TableName", table.TableName);
        WriteString(json, "TableArn", table.TableArn);
        WriteString(json, "TableStatus", GetWireName(table.TableStatus));
        WriteKeySchema(json, "KeySchema", table.KeySchema);
        json.WriteStartArray("AttributeDefinitions");
        foreach (var definition in table.AttributeDefinitions)
        {
            json.WriteStartObject();
            json.WriteString("AttributeName", definition.AttributeName);
            WriteString(json, "AttributeType", GetWireName(definition.AttributeType));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("ItemCount", table.ItemCount);
        json.WriteNumber("TableSizeBytes", table.TableSizeBytes);
        WriteDate(json, "CreationDateTime", table.CreationDateTime);
        if (table.BillingModeSummary is { } billing)
        {
            json.WriteStartObject("BillingModeSummary");
            WriteString(json, "BillingMode", GetWireName(billing.BillingMode));
            WriteDate(json, "LastUpdateToPayPerRequestDateTime", billing.LastUpdateToPayPerRequestDateTime);
            json.WriteEndObject();
        }

        json.WriteStartArray("LocalSecondaryIndexes");
        foreach (var index in table.LocalSecondaryIndexes)
        {
            json.WriteStartObject();
            WriteString(json, "IndexName", index.IndexName);
            WriteKeySchema(json, "KeySchema", index.KeySchema);
            WriteProjection(json, index.Projection);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("GlobalSecondaryIndexes");
        foreach (var index in table.GlobalSecondaryIndexes)
        {
            json.WriteStartObject();
            WriteString(json, "IndexName", index.IndexName);
            WriteString(json, "IndexStatus", GetWireName(index.IndexStatus));
            json.WriteBoolean("Backfilling", index.Backfilling);
            WriteKeySchema(json, "KeySchema", index.KeySchema);
            WriteProjection(json, index.Projection);
            WriteCapacity(json, index.ProvisionedThroughput);
            WriteOnDemand(json, index.OnDemandThroughput);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        if (table.StreamSpecification is { } stream)
        {
            json.WriteStartObject("StreamSpecification");
            json.WriteBoolean("StreamEnabled", stream.StreamEnabled);
            WriteString(json, "StreamViewType", GetWireName(stream.StreamViewType));
            json.WriteEndObject();
        }

        WriteString(json, "LatestStreamArn", table.LatestStreamArn);
        WriteString(json, "LatestStreamLabel", table.LatestStreamLabel);
        json.WriteBoolean("DeletionProtectionEnabled", table.DeletionProtectionEnabled);
        WriteCapacity(json, table.ProvisionedThroughput);
        WriteOnDemand(json, table.OnDemandThroughput);
        if (table.TableClass != TableClass.Unspecified)
        {
            json.WriteStartObject("TableClassSummary");
            WriteString(json, "TableClass", table.TableClass == TableClass.Standard ? "STANDARD" : "STANDARD_INFREQUENT_ACCESS");
            json.WriteEndObject();
        }

        if (table.SSEDescription is { } encryption)
        {
            json.WriteStartObject("SSEDescription");
            WriteString(json, "Status", encryption.Status switch
            {
                SSEStatus.Enabling => "ENABLING",
                SSEStatus.Enabled => "ENABLED",
                SSEStatus.Disabling => "DISABLING",
                SSEStatus.Disabled => "DISABLED",
                SSEStatus.Updating => "UPDATING",
                _ => null,
            });
            WriteString(json, "SSEType", encryption.SSEType switch
            {
                SSEType.Aes256 => "AES256",
                SSEType.Kms => "KMS",
                _ => null,
            });
            WriteString(json, "KMSMasterKeyArn", encryption.KMSMasterKeyArn);
            WriteDate(json, "InaccessibleEncryptionDateTime", encryption.InaccessibleEncryptionDateTime);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    private static void WriteCapacity(Utf8JsonWriter json, ProvisionedThroughputDescription? capacity)
    {
        if (capacity is null)
        {
            return;
        }

        json.WriteStartObject("ProvisionedThroughput");
        json.WriteNumber("ReadCapacityUnits", capacity.ReadCapacityUnits);
        json.WriteNumber("WriteCapacityUnits", capacity.WriteCapacityUnits);
        json.WriteEndObject();
    }

    private static void WriteOnDemand(Utf8JsonWriter json, OnDemandThroughput? maximums)
    {
        if (maximums is null)
        {
            return;
        }

        json.WriteStartObject("OnDemandThroughput");
        if (maximums.MaxReadRequestUnits is { } read)
        {
            json.WriteNumber("MaxReadRequestUnits", read);
        }

        if (maximums.MaxWriteRequestUnits is { } write)
        {
            json.WriteNumber("MaxWriteRequestUnits", write);
        }

        json.WriteEndObject();
    }

    private static void WriteProjection(Utf8JsonWriter json, IndexProjection? projection)
    {
        if (projection is null)
        {
            return;
        }

        json.WriteStartObject("Projection");
        WriteString(json, "ProjectionType", projection.ProjectionType switch
        {
            ProjectionType.All => "ALL",
            ProjectionType.KeysOnly => "KEYS_ONLY",
            ProjectionType.Include => "INCLUDE",
            _ => null,
        });
        if (projection.NonKeyAttributes is { } names)
        {
            json.WriteStartArray("NonKeyAttributes");
            foreach (var name in names)
            {
                json.WriteStringValue(name);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteKeySchema(Utf8JsonWriter json, string name, IReadOnlyList<KeySchemaElement> keySchema)
    {
        json.WriteStartArray(name);
        foreach (var element in keySchema)
        {
            json.WriteStartObject();
            json.WriteString("AttributeName", element.AttributeName);
            WriteString(json, "KeyType", element.KeyType switch
            {
                KeyType.Hash => "HASH",
                KeyType.Range => "RANGE",
                _ => null,
            });
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteDate(Utf8JsonWriter json, string name, DateTimeOffset? value)
    {
        if (value is { } date)
        {
            json.WriteNumber(name, date.ToUnixTimeMilliseconds() / 1000.0);
        }
    }

    private static string? GetWireName(TableStatus value) => value switch
    {
        TableStatus.Creating => "CREATING",
        TableStatus.Updating => "UPDATING",
        TableStatus.Deleting => "DELETING",
        TableStatus.Active => "ACTIVE",
        TableStatus.InaccessibleEncryptionCredentials => "INACCESSIBLE_ENCRYPTION_CREDENTIALS",
        TableStatus.Archiving => "ARCHIVING",
        TableStatus.Archived => "ARCHIVED",
        TableStatus.ReplicationNotAuthorized => "REPLICATION_NOT_AUTHORIZED",
        _ => null,
    };

    private static string? GetWireName(ScalarAttributeType value) => value switch
    {
        ScalarAttributeType.String => "S",
        ScalarAttributeType.Number => "N",
        ScalarAttributeType.Binary => "B",
        _ => null,
    };

    private static string? GetWireName(BillingMode value) => value switch
    {
        BillingMode.Provisioned => "PROVISIONED",
        BillingMode.PayPerRequest => "PAY_PER_REQUEST",
        _ => null,
    };

    private static string? GetWireName(IndexStatus value) => value switch
    {
        IndexStatus.Creating => "CREATING",
        IndexStatus.Updating => "UPDATING",
        IndexStatus.Deleting => "DELETING",
        IndexStatus.Active => "ACTIVE",
        _ => null,
    };

    private static string? GetWireName(StreamViewType value) => value switch
    {
        StreamViewType.NewImage => "NEW_IMAGE",
        StreamViewType.OldImage => "OLD_IMAGE",
        StreamViewType.NewAndOldImages => "NEW_AND_OLD_IMAGES",
        StreamViewType.KeysOnly => "KEYS_ONLY",
        _ => null,
    };
}
