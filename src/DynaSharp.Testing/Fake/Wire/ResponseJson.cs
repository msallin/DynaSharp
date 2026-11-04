using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Writes the parts of response bodies that several operations share: items through the codec of the client, consumed capacity, cancellation reasons, and the one writer of a table
/// description, which <c>DescribeTable</c>, <c>CreateTable</c>, <c>UpdateTable</c>, <c>DeleteTable</c> and the direct API all go through.
/// </summary>
/// <remarks>The members of a table description are in the order of the service model, and what does not apply stays out, as on the wire: no empty index lists, no table class by default.</remarks>
internal static class ResponseJson
{
    /// <summary>Writes an item as a member: <c>"Item":{"pk":{"S":"a"}}</c>.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="member">The member.</param>
    /// <param name="item">The item.</param>
    public static void WriteItem(Utf8JsonWriter json, string member, IReadOnlyDictionary<string, AttributeValue> item)
    {
        ArgumentNullException.ThrowIfNull(json);
        json.WritePropertyName(member);
        AttributeValueConverter.WriteItem(new DynamoDbWriter(json), item);
    }

    /// <summary>Writes items as an array member: <c>"Items":[{...},{...}]</c>.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="member">The member.</param>
    /// <param name="items">The items.</param>
    public static void WriteItems(Utf8JsonWriter json, string member, IEnumerable<IReadOnlyDictionary<string, AttributeValue>> items)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(items);
        json.WriteStartArray(member);
        var writer = new DynamoDbWriter(json);
        foreach (var item in items)
        {
            AttributeValueConverter.WriteItem(writer, item);
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// Writes one consumed capacity as a value, <c>{"TableName":"Orders","CapacityUnits":1.0,"Table":{"CapacityUnits":1.0}}</c>, with only what is set: an operation writes it as
    /// <c>ConsumedCapacity</c>, a batch or a transaction as an element of that array.
    /// </summary>
    /// <param name="json">The writer, where a value goes.</param>
    /// <param name="capacity">The capacity.</param>
    public static void WriteConsumedCapacity(Utf8JsonWriter json, ConsumedCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(capacity);
        json.WriteStartObject();
        OperationJson.WriteStringIfSet(json, "TableName"u8, capacity.TableName);
        WriteUnits(json, capacity.CapacityUnits, capacity.ReadCapacityUnits, capacity.WriteCapacityUnits);
        if (capacity.Table is { } table)
        {
            json.WritePropertyName("Table"u8);
            WriteDetail(json, table);
        }

        WriteDetails(json, "LocalSecondaryIndexes", capacity.LocalSecondaryIndexes);
        WriteDetails(json, "GlobalSecondaryIndexes", capacity.GlobalSecondaryIndexes);
        json.WriteEndObject();
    }

    /// <summary>Writes the reasons of a cancelled transaction as the member <c>CancellationReasons</c>: <c>[{"Code":"ConditionalCheckFailed","Message":"...","Item":{...}},{"Code":"None"}]</c>.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="reasons">The reasons, one per action.</param>
    public static void WriteCancellationReasons(Utf8JsonWriter json, IReadOnlyList<CancellationReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(reasons);
        json.WriteStartArray("CancellationReasons"u8);
        foreach (var reason in reasons)
        {
            json.WriteStartObject();
            OperationJson.WriteStringIfSet(json, "Code"u8, reason.Code);
            OperationJson.WriteStringIfSet(json, "Message"u8, reason.Message);
            OperationJson.WriteItemIfSet(new DynamoDbWriter(json), "Item"u8, reason.Item);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Writes the description of a table as a member: <c>"Table":{...}</c> for <c>DescribeTable</c>, <c>"TableDescription":{...}</c> for the others.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="member">The member.</param>
    /// <param name="table">The table.</param>
    /// <param name="status">The status to report: <c>ACTIVE</c>, or <c>DELETING</c> in the answer of <c>DeleteTable</c>.</param>
    public static void WriteTableDescription(Utf8JsonWriter json, string member, FakeTable table, TableStatus status)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(table);
        json.WriteStartObject(member);
        TableJson.WriteAttributeDefinitions(json, table.AttributeDefinitions);
        json.WriteString("TableName"u8, table.Name);
        TableJson.WriteKeySchema(json, KeyElements(table.KeySchema));
        WireNames.TableStatuses.Write(json, "TableStatus"u8, status);
        json.WriteNumber("CreationDateTime"u8, table.CreationDateTime.ToUnixTimeMilliseconds() / 1000m);
        WriteThroughput(json, table.ReadCapacityUnits, table.WriteCapacityUnits);
        json.WriteNumber("TableSizeBytes"u8, table.SizeBytes);
        json.WriteNumber("ItemCount"u8, table.ItemCount);
        json.WriteString("TableArn"u8, table.Arn);
        json.WriteString("TableId"u8, table.TableId);
        json.WriteStartObject("BillingModeSummary"u8);
        WireNames.BillingModes.Write(json, "BillingMode"u8, table.BillingMode);
        json.WriteEndObject();
        WriteIndexes(json, "LocalSecondaryIndexes", table, table.LocalIndexes);
        WriteIndexes(json, "GlobalSecondaryIndexes", table, table.GlobalIndexes);
        if (table.LatestStream is { } stream)
        {
            TableJson.WriteStreamSpecification(json, stream.IsEnabled ? new StreamSpecification { StreamEnabled = true, StreamViewType = stream.ViewType } : null);
            json.WriteString("LatestStreamLabel"u8, stream.Label);
            json.WriteString("LatestStreamArn"u8, stream.Arn);
        }

        if (table.TableClass != TableClass.Unspecified)
        {
            json.WriteStartObject("TableClassSummary"u8);
            WireNames.TableClasses.Write(json, "TableClass"u8, table.TableClass);
            json.WriteEndObject();
        }

        if (table.KmsMasterKeyArn is { } keyArn)
        {
            json.WriteStartObject("SSEDescription"u8);
            WireNames.SSEStatuses.Write(json, "Status"u8, SSEStatus.Enabled);
            WireNames.SSETypes.Write(json, "SSEType"u8, SSEType.Kms);
            json.WriteString("KMSMasterKeyArn"u8, keyArn);
            json.WriteEndObject();
        }

        json.WriteBoolean("DeletionProtectionEnabled"u8, table.DeletionProtectionEnabled);
        TableJson.WriteOnDemandThroughput(json, table.OnDemandThroughput);
        json.WriteEndObject();
    }

    /// <summary>Gets a key schema in the form of the request records.</summary>
    /// <param name="keySchema">The key.</param>
    /// <returns>The elements, <c>HASH</c> first.</returns>
    public static IReadOnlyList<KeySchemaElement> KeyElements(KeySchema keySchema)
    {
        ArgumentNullException.ThrowIfNull(keySchema);
        return keySchema.SortKeyName is null
            ? [new KeySchemaElement(keySchema.PartitionKeyName, KeyType.Hash)]
            : [new KeySchemaElement(keySchema.PartitionKeyName, KeyType.Hash), new KeySchemaElement(keySchema.SortKeyName, KeyType.Range)];
    }

    private static void WriteIndexes(Utf8JsonWriter json, string member, FakeTable table, List<FakeIndex> indexes)
    {
        if (indexes.Count == 0)
        {
            return;
        }

        json.WriteStartArray(member);
        foreach (var index in indexes)
        {
            json.WriteStartObject();
            json.WriteString("IndexName"u8, index.Name);
            TableJson.WriteKeySchema(json, KeyElements(index.KeySchema));
            TableJson.WriteProjection(json, index.Projection);
            if (index.IsGlobal)
            {
                WireNames.IndexStatuses.Write(json, "IndexStatus"u8, IndexStatus.Active);
                WriteThroughput(json, index.ReadCapacityUnits, index.WriteCapacityUnits);
            }

            json.WriteNumber("IndexSizeBytes"u8, index.SizeBytes);
            json.WriteNumber("ItemCount"u8, index.ItemCount);
            json.WriteString("IndexArn"u8, table.Arn + "/index/" + index.Name);
            TableJson.WriteOnDemandThroughput(json, index.OnDemandThroughput);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteThroughput(Utf8JsonWriter json, long read, long write)
    {
        json.WriteStartObject("ProvisionedThroughput"u8);
        json.WriteNumber("NumberOfDecreasesToday"u8, 0);
        json.WriteNumber("ReadCapacityUnits"u8, read);
        json.WriteNumber("WriteCapacityUnits"u8, write);
        json.WriteEndObject();
    }

    private static void WriteDetails(Utf8JsonWriter json, string member, IReadOnlyDictionary<string, CapacityDetail>? details)
    {
        if (details is null)
        {
            return;
        }

        json.WriteStartObject(member);
        foreach (var (name, detail) in details)
        {
            json.WritePropertyName(name);
            WriteDetail(json, detail);
        }

        json.WriteEndObject();
    }

    private static void WriteDetail(Utf8JsonWriter json, CapacityDetail detail)
    {
        json.WriteStartObject();
        WriteUnits(json, detail.CapacityUnits, detail.ReadCapacityUnits, detail.WriteCapacityUnits);
        json.WriteEndObject();
    }

    private static void WriteUnits(Utf8JsonWriter json, double? capacityUnits, double? readCapacityUnits, double? writeCapacityUnits)
    {
        WriteIfSet(json, "CapacityUnits"u8, capacityUnits);
        WriteIfSet(json, "ReadCapacityUnits"u8, readCapacityUnits);
        WriteIfSet(json, "WriteCapacityUnits"u8, writeCapacityUnits);
    }

    private static void WriteIfSet(Utf8JsonWriter json, ReadOnlySpan<byte> member, double? value)
    {
        if (value is { } units)
        {
            json.WriteNumber(member, units);
        }
    }
}
