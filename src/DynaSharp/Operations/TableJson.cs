using System.Collections.ObjectModel;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The JSON of the table structures that several operations share. Read: the table description that <c>DescribeTable</c>,
/// <c>CreateTable</c>, <c>UpdateTable</c> and <c>DeleteTable</c> return, and the key schema, which DynamoDB Streams reports as well.
/// Written: what <c>CreateTable</c> and <c>UpdateTable</c> have in common.
/// </summary>
/// <remarks>
/// The wire names of the enums are in <see cref="WireNames"/>.
/// </remarks>
internal static class TableJson
{
    /// <summary>Reads the response of <c>CreateTable</c>, <c>UpdateTable</c> or <c>DeleteTable</c>: an object with the table description.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="operationName">The operation, for the message when the description is missing.</param>
    /// <returns>The table description.</returns>
    public static TableDescription ReadTableDescriptionEnvelope(ref DynamoDbReader reader, string operationName)
    {
        TableDescription? table = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TableDescription"u8))
            {
                table = ReadTable(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return table ?? throw new JsonException($"The {operationName} response has no TableDescription.");
    }

    /// <summary>Reads a table description object; properties that are not known are skipped.</summary>
    /// <param name="reader">The reader, positioned before the object.</param>
    /// <returns>The table description.</returns>
    public static TableDescription ReadTable(ref DynamoDbReader reader)
    {
        string? tableName = null;
        string? tableArn = null;
        var tableStatus = TableStatus.Unknown;
        IReadOnlyList<KeySchemaElement> keySchema = [];
        IReadOnlyList<AttributeDefinition> attributeDefinitions = [];
        long itemCount = 0;
        long tableSizeBytes = 0;
        DateTimeOffset? creationDateTime = null;
        BillingModeSummary? billingModeSummary = null;
        IReadOnlyList<LocalSecondaryIndexDescription> localSecondaryIndexes = [];
        IReadOnlyList<GlobalSecondaryIndexDescription> globalSecondaryIndexes = [];
        StreamSpecification? streamSpecification = null;
        string? latestStreamArn = null;
        string? latestStreamLabel = null;
        var deletionProtectionEnabled = false;
        ProvisionedThroughputDescription? provisionedThroughput = null;
        var tableClass = TableClass.Unspecified;

        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TableName"u8))
            {
                tableName = OperationJson.ReadRequiredString(ref reader);
            }
            else if (reader.AttributeNameEquals("TableArn"u8))
            {
                tableArn = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("TableStatus"u8))
            {
                tableStatus = OperationJson.ReadEnum(ref reader, WireNames.TableStatuses);
            }
            else if (reader.AttributeNameEquals("KeySchema"u8))
            {
                keySchema = ReadKeySchema(ref reader);
            }
            else if (reader.AttributeNameEquals("AttributeDefinitions"u8))
            {
                attributeDefinitions = ReadAttributeDefinitions(ref reader);
            }
            else if (reader.AttributeNameEquals("ItemCount"u8))
            {
                itemCount = OperationJson.ReadInt64(ref reader);
            }
            else if (reader.AttributeNameEquals("TableSizeBytes"u8))
            {
                tableSizeBytes = OperationJson.ReadInt64(ref reader);
            }
            else if (reader.AttributeNameEquals("CreationDateTime"u8))
            {
                creationDateTime = OperationJson.ReadEpochSeconds(ref reader);
            }
            else if (reader.AttributeNameEquals("BillingModeSummary"u8))
            {
                billingModeSummary = ReadBillingModeSummary(ref reader);
            }
            else if (reader.AttributeNameEquals("LocalSecondaryIndexes"u8))
            {
                localSecondaryIndexes = ReadLocalSecondaryIndexes(ref reader);
            }
            else if (reader.AttributeNameEquals("GlobalSecondaryIndexes"u8))
            {
                globalSecondaryIndexes = ReadGlobalSecondaryIndexes(ref reader);
            }
            else if (reader.AttributeNameEquals("StreamSpecification"u8))
            {
                streamSpecification = ReadStreamSpecification(ref reader);
            }
            else if (reader.AttributeNameEquals("LatestStreamArn"u8))
            {
                latestStreamArn = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("LatestStreamLabel"u8))
            {
                latestStreamLabel = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("DeletionProtectionEnabled"u8))
            {
                deletionProtectionEnabled = OperationJson.ReadBoolean(ref reader);
            }
            else if (reader.AttributeNameEquals("ProvisionedThroughput"u8))
            {
                provisionedThroughput = ReadProvisionedThroughput(ref reader);
            }
            else if (reader.AttributeNameEquals("TableClassSummary"u8))
            {
                tableClass = ReadTableClassSummary(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new TableDescription
        {
            TableName = tableName ?? throw new JsonException("The table description has no TableName."),
            TableArn = tableArn,
            TableStatus = tableStatus,
            KeySchema = keySchema,
            AttributeDefinitions = attributeDefinitions,
            ItemCount = itemCount,
            TableSizeBytes = tableSizeBytes,
            CreationDateTime = creationDateTime,
            BillingModeSummary = billingModeSummary,
            LocalSecondaryIndexes = localSecondaryIndexes,
            GlobalSecondaryIndexes = globalSecondaryIndexes,
            StreamSpecification = streamSpecification,
            LatestStreamArn = latestStreamArn,
            LatestStreamLabel = latestStreamLabel,
            DeletionProtectionEnabled = deletionProtectionEnabled,
            ProvisionedThroughput = provisionedThroughput,
            TableClass = tableClass,
        };
    }

    /// <summary>Reads a key schema: an array of <c>{"AttributeName":"pk","KeyType":"HASH"}</c>.</summary>
    /// <param name="reader">The reader, positioned before the array.</param>
    /// <returns>The key schema.</returns>
    public static ReadOnlyCollection<KeySchemaElement> ReadKeySchema(ref DynamoDbReader reader)
    {
        var elements = new List<KeySchemaElement>();
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            var attributeName = string.Empty;
            var keyType = KeyType.Unknown;
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("AttributeName"u8))
                {
                    attributeName = OperationJson.ReadRequiredString(ref reader);
                }
                else if (reader.AttributeNameEquals("KeyType"u8))
                {
                    keyType = OperationJson.ReadEnum(ref reader, WireNames.KeyTypes);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }

            elements.Add(new KeySchemaElement(attributeName, keyType));
        }

        return elements.AsReadOnly();
    }

    /// <summary>Writes the <c>AttributeDefinitions</c> of a table.</summary>
    /// <param name="json">The writer.</param>
    /// <param name="definitions">The definitions.</param>
    public static void WriteAttributeDefinitions(Utf8JsonWriter json, IReadOnlyList<AttributeDefinition> definitions)
    {
        json.WriteStartArray("AttributeDefinitions"u8);
        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            json.WriteStartObject();
            json.WriteString("AttributeName"u8, definition.AttributeName);
            WireNames.ScalarAttributeTypes.Write(json, "AttributeType"u8, definition.AttributeType);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Writes the members of a new global secondary index, into an object the caller opens and closes.</summary>
    /// <param name="json">The writer.</param>
    /// <param name="index">The index.</param>
    public static void WriteGlobalSecondaryIndexMembers(Utf8JsonWriter json, GlobalSecondaryIndex index)
    {
        json.WriteString("IndexName"u8, index.IndexName);
        WriteKeySchema(json, index.KeySchema);
        WriteProjection(json, index.Projection);
        WriteProvisionedThroughput(json, index.ProvisionedThroughput);
    }

    public static void WriteKeySchema(Utf8JsonWriter json, IReadOnlyList<KeySchemaElement> keySchema)
    {
        json.WriteStartArray("KeySchema"u8);
        for (var i = 0; i < keySchema.Count; i++)
        {
            var element = keySchema[i];
            json.WriteStartObject();
            json.WriteString("AttributeName"u8, element.AttributeName);
            WireNames.KeyTypes.Write(json, "KeyType"u8, element.KeyType);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    public static void WriteProjection(Utf8JsonWriter json, IndexProjection projection)
    {
        json.WriteStartObject("Projection"u8);
        WireNames.ProjectionTypes.Write(json, "ProjectionType"u8, projection.ProjectionType);
        if (projection.NonKeyAttributes is { Count: > 0 } nonKeyAttributes)
        {
            json.WriteStartArray("NonKeyAttributes"u8);
            for (var i = 0; i < nonKeyAttributes.Count; i++)
            {
                json.WriteStringValue(nonKeyAttributes[i]);
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    public static void WriteProvisionedThroughput(Utf8JsonWriter json, ProvisionedThroughput? throughput)
    {
        if (throughput is null)
        {
            return;
        }

        json.WriteStartObject("ProvisionedThroughput"u8);
        json.WriteNumber("ReadCapacityUnits"u8, throughput.ReadCapacityUnits);
        json.WriteNumber("WriteCapacityUnits"u8, throughput.WriteCapacityUnits);
        json.WriteEndObject();
    }

    public static void WriteStreamSpecification(Utf8JsonWriter json, StreamSpecification? specification)
    {
        if (specification is null)
        {
            return;
        }

        json.WriteStartObject("StreamSpecification"u8);
        json.WriteBoolean("StreamEnabled"u8, specification.StreamEnabled);
        WireNames.StreamViewTypes.WriteIfSet(json, "StreamViewType"u8, specification.StreamViewType);
        json.WriteEndObject();
    }

    private static ReadOnlyCollection<AttributeDefinition> ReadAttributeDefinitions(ref DynamoDbReader reader)
    {
        var definitions = new List<AttributeDefinition>();
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            var attributeName = string.Empty;
            var attributeType = ScalarAttributeType.Unknown;
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("AttributeName"u8))
                {
                    attributeName = OperationJson.ReadRequiredString(ref reader);
                }
                else if (reader.AttributeNameEquals("AttributeType"u8))
                {
                    attributeType = OperationJson.ReadEnum(ref reader, WireNames.ScalarAttributeTypes);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }

            definitions.Add(new AttributeDefinition(attributeName, attributeType));
        }

        return definitions.AsReadOnly();
    }

    private static BillingModeSummary ReadBillingModeSummary(ref DynamoDbReader reader)
    {
        var billingMode = BillingMode.Unspecified;
        DateTimeOffset? lastUpdate = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("BillingMode"u8))
            {
                billingMode = OperationJson.ReadEnum(ref reader, WireNames.BillingModes);
            }
            else if (reader.AttributeNameEquals("LastUpdateToPayPerRequestDateTime"u8))
            {
                lastUpdate = OperationJson.ReadEpochSeconds(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new BillingModeSummary { BillingMode = billingMode, LastUpdateToPayPerRequestDateTime = lastUpdate };
    }

    private static ProvisionedThroughputDescription ReadProvisionedThroughput(ref DynamoDbReader reader)
    {
        long readCapacityUnits = 0;
        long writeCapacityUnits = 0;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("ReadCapacityUnits"u8))
            {
                readCapacityUnits = OperationJson.ReadInt64(ref reader);
            }
            else if (reader.AttributeNameEquals("WriteCapacityUnits"u8))
            {
                writeCapacityUnits = OperationJson.ReadInt64(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new ProvisionedThroughputDescription { ReadCapacityUnits = readCapacityUnits, WriteCapacityUnits = writeCapacityUnits };
    }

    private static TableClass ReadTableClassSummary(ref DynamoDbReader reader)
    {
        var tableClass = TableClass.Unspecified;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TableClass"u8))
            {
                tableClass = OperationJson.ReadEnum(ref reader, WireNames.TableClasses);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return tableClass;
    }

    private static StreamSpecification ReadStreamSpecification(ref DynamoDbReader reader)
    {
        var streamEnabled = false;
        var streamViewType = StreamViewType.Unspecified;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("StreamEnabled"u8))
            {
                streamEnabled = OperationJson.ReadBoolean(ref reader);
            }
            else if (reader.AttributeNameEquals("StreamViewType"u8))
            {
                streamViewType = OperationJson.ReadEnum(ref reader, WireNames.StreamViewTypes);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new StreamSpecification { StreamEnabled = streamEnabled, StreamViewType = streamViewType };
    }

    private static ReadOnlyCollection<LocalSecondaryIndexDescription> ReadLocalSecondaryIndexes(ref DynamoDbReader reader)
    {
        var indexes = new List<LocalSecondaryIndexDescription>();
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            string? indexName = null;
            IReadOnlyList<KeySchemaElement> keySchema = [];
            IndexProjection? projection = null;
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("IndexName"u8))
                {
                    indexName = OperationJson.ReadRequiredString(ref reader);
                }
                else if (reader.AttributeNameEquals("KeySchema"u8))
                {
                    keySchema = ReadKeySchema(ref reader);
                }
                else if (reader.AttributeNameEquals("Projection"u8))
                {
                    projection = ReadProjection(ref reader);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }

            indexes.Add(new LocalSecondaryIndexDescription
            {
                IndexName = indexName ?? throw new JsonException("A local secondary index has no IndexName."),
                KeySchema = keySchema,
                Projection = projection,
            });
        }

        return indexes.AsReadOnly();
    }

    private static ReadOnlyCollection<GlobalSecondaryIndexDescription> ReadGlobalSecondaryIndexes(ref DynamoDbReader reader)
    {
        var indexes = new List<GlobalSecondaryIndexDescription>();
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            string? indexName = null;
            var indexStatus = IndexStatus.Unknown;
            var backfilling = false;
            IReadOnlyList<KeySchemaElement> keySchema = [];
            IndexProjection? projection = null;
            ProvisionedThroughputDescription? provisionedThroughput = null;
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("IndexName"u8))
                {
                    indexName = OperationJson.ReadRequiredString(ref reader);
                }
                else if (reader.AttributeNameEquals("ProvisionedThroughput"u8))
                {
                    provisionedThroughput = ReadProvisionedThroughput(ref reader);
                }
                else if (reader.AttributeNameEquals("IndexStatus"u8))
                {
                    indexStatus = OperationJson.ReadEnum(ref reader, WireNames.IndexStatuses);
                }
                else if (reader.AttributeNameEquals("Backfilling"u8))
                {
                    backfilling = OperationJson.ReadBoolean(ref reader);
                }
                else if (reader.AttributeNameEquals("KeySchema"u8))
                {
                    keySchema = ReadKeySchema(ref reader);
                }
                else if (reader.AttributeNameEquals("Projection"u8))
                {
                    projection = ReadProjection(ref reader);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }

            indexes.Add(new GlobalSecondaryIndexDescription
            {
                IndexName = indexName ?? throw new JsonException("A global secondary index has no IndexName."),
                IndexStatus = indexStatus,
                Backfilling = backfilling,
                KeySchema = keySchema,
                Projection = projection,
                ProvisionedThroughput = provisionedThroughput,
            });
        }

        return indexes.AsReadOnly();
    }

    private static IndexProjection ReadProjection(ref DynamoDbReader reader)
    {
        var projectionType = ProjectionType.Unknown;
        IReadOnlyList<string>? nonKeyAttributes = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("ProjectionType"u8))
            {
                projectionType = OperationJson.ReadEnum(ref reader, WireNames.ProjectionTypes);
            }
            else if (reader.AttributeNameEquals("NonKeyAttributes"u8))
            {
                nonKeyAttributes = OperationJson.ReadStringArray(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new IndexProjection { ProjectionType = projectionType, NonKeyAttributes = nonKeyAttributes };
    }
}
