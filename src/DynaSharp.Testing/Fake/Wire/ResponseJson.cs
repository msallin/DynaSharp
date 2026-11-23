using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Streams;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Writes the parts of response bodies that several operations share: items through the codec of the client, consumed capacity, cancellation reasons, and the one writer of a table
/// description, which <c>DescribeTable</c>, <c>CreateTable</c>, <c>UpdateTable</c>, <c>DeleteTable</c>, the direct API and the table answers of <see cref="StubResponse"/> all go through.
/// </summary>
/// <remarks>The members of a table description are in the order of the service model, and what does not apply stays out, as on the wire: no empty index lists, no table class by default.</remarks>
internal static class ResponseJson
{
    /// <summary>The <c>eventVersion</c> of every record of DynamoDB Streams.</summary>
    public const string RecordEventVersion = "1.1";

    /// <summary>The <c>eventSource</c> of every record of DynamoDB Streams.</summary>
    public const string RecordEventSource = "aws:dynamodb";

    /// <summary>Writes an item as a member: <c>"Item":{"pk":{"S":"a"}}</c>.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="member">The member.</param>
    /// <param name="item">The item.</param>
    public static void WriteItem(Utf8JsonWriter json, string member, IReadOnlyDictionary<string, AttributeValue> item)
    {
        json.WritePropertyName(member);
        AttributeValueConverter.WriteItem(new DynamoDbWriter(json), item);
    }

    /// <summary>Writes items as an array member: <c>"Items":[{...},{...}]</c>.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="member">The member.</param>
    /// <param name="items">The items.</param>
    public static void WriteItems(Utf8JsonWriter json, string member, IEnumerable<IReadOnlyDictionary<string, AttributeValue>> items)
    {
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

    /// <summary>Writes the description of a table of the fake as a member, through the writer the stubs use as well.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="member">The member: <c>Table</c> for <c>DescribeTable</c>, <c>TableDescription</c> for the others.</param>
    /// <param name="table">The table.</param>
    /// <param name="status">The status to report: <c>ACTIVE</c>, or <c>DELETING</c> in the answer of <c>DeleteTable</c>.</param>
    public static void WriteTableDescription(Utf8JsonWriter json, string member, FakeTable table, TableStatus status)
    {
        var sizes = new Dictionary<string, IndexSize>(table.LocalIndexes.Count + table.GlobalIndexes.Count, StringComparer.Ordinal);
        foreach (var index in table.LocalIndexes)
        {
            sizes[index.Name] = new IndexSize(index.SizeBytes, index.ItemCount);
        }

        foreach (var index in table.GlobalIndexes)
        {
            sizes[index.Name] = new IndexSize(index.SizeBytes, index.ItemCount);
        }

        WriteTableDescription(json, member, Describe(table, status), table.TableId, sizes);
    }

    /// <summary>
    /// Writes the description of a table as a member: <c>"Table":{...}</c> for <c>DescribeTable</c>, <c>"TableDescription":{...}</c> for the others. The one writer of this shape,
    /// for the fake and for <see cref="StubResponse"/>.
    /// </summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="member">The member.</param>
    /// <param name="table">The description, with its name, ARN, creation time and billing mode set. A throughput that is not set is written as zero, which a table on demand reports.</param>
    /// <param name="tableId">The value of <c>TableId</c>, which the description of the client does not carry.</param>
    /// <param name="indexSizes">The size and item count of each index by name, which the description of the client does not carry, or <see langword="null"/> for empty indexes.</param>
    public static void WriteTableDescription(Utf8JsonWriter json, string member, TableDescription table, string tableId, IReadOnlyDictionary<string, IndexSize>? indexSizes)
    {
        var arn = table.TableArn!;
        json.WriteStartObject(member);
        TableJson.WriteAttributeDefinitions(json, table.AttributeDefinitions);
        json.WriteString("TableName"u8, table.TableName);
        TableJson.WriteKeySchema(json, table.KeySchema);
        WireNames.TableStatuses.Write(json, "TableStatus"u8, table.TableStatus);
        json.WriteNumber("CreationDateTime"u8, table.CreationDateTime!.Value.ToUnixTimeMilliseconds() / 1000m);
        WriteThroughput(json, table.ProvisionedThroughput);
        json.WriteNumber("TableSizeBytes"u8, table.TableSizeBytes);
        json.WriteNumber("ItemCount"u8, table.ItemCount);
        json.WriteString("TableArn"u8, arn);
        json.WriteString("TableId"u8, tableId);
        json.WriteStartObject("BillingModeSummary"u8);
        WireNames.BillingModes.Write(json, "BillingMode"u8, table.BillingModeSummary!.BillingMode);
        json.WriteEndObject();
        if (table.LocalSecondaryIndexes.Count > 0)
        {
            json.WriteStartArray("LocalSecondaryIndexes"u8);
            foreach (var index in table.LocalSecondaryIndexes)
            {
                json.WriteStartObject();
                json.WriteString("IndexName"u8, index.IndexName);
                TableJson.WriteKeySchema(json, index.KeySchema);
                TableJson.WriteProjection(json, index.Projection!);
                WriteIndexSizeAndArn(json, arn, index.IndexName!, indexSizes);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        if (table.GlobalSecondaryIndexes.Count > 0)
        {
            json.WriteStartArray("GlobalSecondaryIndexes"u8);
            foreach (var index in table.GlobalSecondaryIndexes)
            {
                json.WriteStartObject();
                json.WriteString("IndexName"u8, index.IndexName);
                TableJson.WriteKeySchema(json, index.KeySchema);
                if (index.Projection is { } projection)
                {
                    TableJson.WriteProjection(json, projection);
                }

                WireNames.IndexStatuses.Write(json, "IndexStatus"u8, index.IndexStatus);
                OperationJson.WriteIfTrue(json, "Backfilling"u8, index.Backfilling);
                WriteThroughput(json, index.ProvisionedThroughput);
                WriteIndexSizeAndArn(json, arn, index.IndexName!, indexSizes);
                TableJson.WriteOnDemandThroughput(json, index.OnDemandThroughput);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        if (table.LatestStreamArn is { } streamArn)
        {
            TableJson.WriteStreamSpecification(json, table.StreamSpecification);
            json.WriteString("LatestStreamLabel"u8, table.LatestStreamLabel);
            json.WriteString("LatestStreamArn"u8, streamArn);
        }

        if (table.TableClass != TableClass.Unspecified)
        {
            json.WriteStartObject("TableClassSummary"u8);
            WireNames.TableClasses.Write(json, "TableClass"u8, table.TableClass);
            json.WriteEndObject();
        }

        if (table.SSEDescription is { } sse)
        {
            json.WriteStartObject("SSEDescription"u8);
            WireNames.SSEStatuses.Write(json, "Status"u8, sse.Status);
            WireNames.SSETypes.Write(json, "SSEType"u8, sse.SSEType);
            json.WriteString("KMSMasterKeyArn"u8, sse.KMSMasterKeyArn);
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
        return keySchema.SortKeyName is null
            ? [new KeySchemaElement(keySchema.PartitionKeyName, KeyType.Hash)]
            : [new KeySchemaElement(keySchema.PartitionKeyName, KeyType.Hash), new KeySchemaElement(keySchema.SortKeyName, KeyType.Range)];
    }

    /// <summary>Writes <c>ProvisionedThroughput</c> of a table or an index, zero where it is not set, which a table on demand reports.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="throughput">The throughput, or <see langword="null"/> for zero.</param>
    public static void WriteThroughput(Utf8JsonWriter json, ProvisionedThroughputDescription? throughput)
    {
        json.WriteStartObject("ProvisionedThroughput"u8);
        json.WriteNumber("NumberOfDecreasesToday"u8, 0);
        json.WriteNumber("ReadCapacityUnits"u8, throughput?.ReadCapacityUnits ?? 0);
        json.WriteNumber("WriteCapacityUnits"u8, throughput?.WriteCapacityUnits ?? 0);
        json.WriteEndObject();
    }

    /// <summary>Writes the answer of <c>DescribeStream</c> as the member <c>StreamDescription</c>, with what the description holds: the one writer of this shape, for the fake and the stubs.</summary>
    /// <param name="json">The writer, inside an object.</param>
    /// <param name="description">The description.</param>
    public static void WriteStreamDescription(Utf8JsonWriter json, StreamDescription description)
    {
        json.WriteStartObject("StreamDescription"u8);
        OperationJson.WriteStringIfSet(json, "StreamArn"u8, description.StreamArn);
        OperationJson.WriteStringIfSet(json, "StreamLabel"u8, description.StreamLabel);
        WireNames.StreamStatuses.WriteIfSet(json, "StreamStatus"u8, description.StreamStatus);
        WireNames.StreamViewTypes.WriteIfSet(json, "StreamViewType"u8, description.StreamViewType);
        WriteEpochSecondsIfSet(json, "CreationRequestDateTime"u8, description.CreationRequestDateTime);
        OperationJson.WriteStringIfSet(json, "TableName"u8, description.TableName);
        TableJson.WriteKeySchema(json, description.KeySchema);
        json.WriteStartArray("Shards"u8);
        foreach (var shard in description.Shards)
        {
            json.WriteStartObject();
            OperationJson.WriteStringIfSet(json, "ShardId"u8, shard.ShardId);
            if (shard.SequenceNumberRange is { } range)
            {
                json.WriteStartObject("SequenceNumberRange"u8);
                OperationJson.WriteStringIfSet(json, "StartingSequenceNumber"u8, range.StartingSequenceNumber);
                OperationJson.WriteStringIfSet(json, "EndingSequenceNumber"u8, range.EndingSequenceNumber);
                json.WriteEndObject();
            }

            OperationJson.WriteStringIfSet(json, "ParentShardId"u8, shard.ParentShardId);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        OperationJson.WriteStringIfSet(json, "LastEvaluatedShardId"u8, description.LastEvaluatedShardId);
        json.WriteEndObject();
    }

    /// <summary>
    /// Writes one record of <c>GetRecords</c> as a value, with what it holds: <c>{"eventID":"1","eventName":"INSERT",...,"dynamodb":{...}}</c>, the one structure of the service model
    /// that spells its members in lower case. The one writer of this shape, for the fake and the stubs.
    /// </summary>
    /// <param name="json">The writer, where a value goes.</param>
    /// <param name="record">The record.</param>
    public static void WriteChangeRecord(Utf8JsonWriter json, ChangeRecord record)
    {
        json.WriteStartObject();
        OperationJson.WriteStringIfSet(json, "eventID"u8, record.EventId);
        WireNames.OperationTypes.WriteIfSet(json, "eventName"u8, record.EventName);
        OperationJson.WriteStringIfSet(json, "eventVersion"u8, record.EventVersion);
        OperationJson.WriteStringIfSet(json, "eventSource"u8, record.EventSource);
        OperationJson.WriteStringIfSet(json, "awsRegion"u8, record.Region);
        if (record.DynamoDb is { } change)
        {
            json.WriteStartObject("dynamodb"u8);
            WriteEpochSecondsIfSet(json, "ApproximateCreationDateTime"u8, change.ApproximateCreationDateTime);
            var writer = new DynamoDbWriter(json);
            OperationJson.WriteItemIfSet(writer, "Keys"u8, change.Keys);
            OperationJson.WriteItemIfSet(writer, "NewImage"u8, change.NewImage);
            OperationJson.WriteItemIfSet(writer, "OldImage"u8, change.OldImage);
            OperationJson.WriteStringIfSet(json, "SequenceNumber"u8, change.SequenceNumber);
            if (change.SizeBytes is { } size)
            {
                json.WriteNumber("SizeBytes"u8, size);
            }

            WireNames.StreamViewTypes.WriteIfSet(json, "StreamViewType"u8, change.StreamViewType);
            json.WriteEndObject();
        }

        if (record.UserIdentity is { } identity)
        {
            json.WriteStartObject("userIdentity"u8);
            OperationJson.WriteStringIfSet(json, "PrincipalId"u8, identity.PrincipalId);
            OperationJson.WriteStringIfSet(json, "Type"u8, identity.Type);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    private static TableDescription Describe(FakeTable table, TableStatus status)
    {
        var locals = new LocalSecondaryIndexDescription[table.LocalIndexes.Count];
        for (var i = 0; i < locals.Length; i++)
        {
            var index = table.LocalIndexes[i];
            locals[i] = new LocalSecondaryIndexDescription { IndexName = index.Name, KeySchema = KeyElements(index.KeySchema), Projection = index.Projection };
        }

        var globals = new GlobalSecondaryIndexDescription[table.GlobalIndexes.Count];
        for (var i = 0; i < globals.Length; i++)
        {
            var index = table.GlobalIndexes[i];
            globals[i] = new GlobalSecondaryIndexDescription
            {
                IndexName = index.Name,
                IndexStatus = IndexStatus.Active,
                KeySchema = KeyElements(index.KeySchema),
                Projection = index.Projection,
                ProvisionedThroughput = new ProvisionedThroughputDescription { ReadCapacityUnits = index.ReadCapacityUnits, WriteCapacityUnits = index.WriteCapacityUnits },
                OnDemandThroughput = index.OnDemandThroughput,
            };
        }

        var stream = table.LatestStream;
        return new TableDescription
        {
            TableName = table.Name,
            TableArn = table.Arn,
            TableStatus = status,
            KeySchema = KeyElements(table.KeySchema),
            AttributeDefinitions = table.AttributeDefinitions,
            ItemCount = table.ItemCount,
            TableSizeBytes = table.SizeBytes,
            CreationDateTime = table.CreationDateTime,
            BillingModeSummary = new BillingModeSummary { BillingMode = table.BillingMode },
            ProvisionedThroughput = new ProvisionedThroughputDescription { ReadCapacityUnits = table.ReadCapacityUnits, WriteCapacityUnits = table.WriteCapacityUnits },
            OnDemandThroughput = table.OnDemandThroughput,
            TableClass = table.TableClass,
            LocalSecondaryIndexes = locals,
            GlobalSecondaryIndexes = globals,
            StreamSpecification = stream is { IsEnabled: true } ? new StreamSpecification { StreamEnabled = true, StreamViewType = stream.ViewType } : null,
            LatestStreamArn = stream?.Arn,
            LatestStreamLabel = stream?.Label,
            SSEDescription = table.KmsMasterKeyArn is { } keyArn ? new SSEDescription { Status = SSEStatus.Enabled, SSEType = SSEType.Kms, KMSMasterKeyArn = keyArn } : null,
            DeletionProtectionEnabled = table.DeletionProtectionEnabled,
        };
    }

    private static void WriteIndexSizeAndArn(Utf8JsonWriter json, string tableArn, string indexName, IReadOnlyDictionary<string, IndexSize>? indexSizes)
    {
        var size = indexSizes is null ? default : indexSizes[indexName];
        json.WriteNumber("IndexSizeBytes"u8, size.Bytes);
        json.WriteNumber("ItemCount"u8, size.Items);
        json.WriteString("IndexArn"u8, tableArn + "/index/" + indexName);
    }

    // The service model types these as timestamps, which this protocol sends as seconds since the epoch, with a fraction when there is one: 1767225600 or 1767225600.125.
    private static void WriteEpochSecondsIfSet(Utf8JsonWriter json, ReadOnlySpan<byte> member, DateTimeOffset? value)
    {
        if (value is { } time)
        {
            json.WriteNumber(member, time.ToUnixTimeMilliseconds() / 1000m);
        }
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

    /// <summary>The size of an index and the items it holds, which a table description reports and the description of the client does not carry.</summary>
    /// <param name="Bytes">The size in bytes.</param>
    /// <param name="Items">The number of items.</param>
    internal readonly record struct IndexSize(long Bytes, long Items);
}
