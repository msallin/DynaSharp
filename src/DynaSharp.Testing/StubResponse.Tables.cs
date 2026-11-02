using System.Text.Json;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Testing;

/// <content>The answers to table administration. A table made up here is on demand, empty, and has the key <c>pk</c> unless an entity says otherwise.</content>
public sealed partial class StubResponse
{
    private const string DefaultKey = "pk";

    // The sample account of the AWS documentation, in the region of StubClient, so that an ARN reads the same in every test.
    private const string ArnPrefix = "arn:aws:dynamodb:" + StubClient.RegionName + ":123456789012:table/";
    private const string TableId = "00000000-0000-4000-8000-000000000000";

    // 2026-01-01T00:00:00Z, the same in every answer: a canned response that read the clock would not be deterministic.
    private const long CreationEpochSeconds = 1767225600;

    /// <summary>
    /// Creates the answer to a <c>DescribeTable</c>: a table on demand and empty, with a string partition key <c>pk</c>, the indexes given and their key attributes,
    /// <c>{"Table":{...}}</c>. The arguments say what a wait or a check reads, and the rest is what DynamoDB sends for such a table, so that the body stays one DynamoDB could have sent.
    /// </summary>
    /// <remarks>
    /// A table reads <c>ACTIVE</c> while an index it was given later is built, which is what a wait looks at: <c>Table("Orders", TableStatus.Active, GlobalIndex("ByCustomer", IndexStatus.Creating))</c>, queued in front of
    /// <c>Table("Orders", TableStatus.Active, GlobalIndex("ByCustomer"))</c>. <see cref="Table{T}"/> takes the key and the indexes from an entity. Other values, such as <c>LatestStreamArn</c>, are a body for <see cref="Json"/>.
    /// </remarks>
    /// <param name="name">The table name.</param>
    /// <param name="status">The status of the table.</param>
    /// <param name="globalIndexes">The global secondary indexes, as <see cref="GlobalIndex"/> makes them.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> or the status of an index is <c>Unknown</c>, which has no name on the wire, or not a member of its enum.</exception>
    public static StubResponse Table(string name, TableStatus status = TableStatus.Active, params ReadOnlySpan<GlobalSecondaryIndexDescription> globalIndexes)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TableAnswer("Table", MadeUpTable(name, status, globalIndexes));
    }

    /// <summary>
    /// Creates the answer to a <c>DescribeTable</c> of a table an entity lives in: its keys with their types, every global index it declares as active, and its local indexes, as
    /// <see cref="CreateTableCommand"/> would have created them. A wait for the table ends on this answer.
    /// </summary>
    /// <remarks>
    /// A table of a single-table schema has the indexes of all its entities, and this lists those of one. For the others, or for an index that is not active yet, list them by name with <see cref="Table(string, TableStatus, ReadOnlySpan{GlobalSecondaryIndexDescription})"/>.
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="name">The table name.</param>
    /// <param name="status">The status of the table.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is <see cref="TableStatus.Unknown"/>, which has no name on the wire, or not a member of the enum.</exception>
    public static StubResponse Table<T>(string name, TableStatus status = TableStatus.Active)
        where T : IDynamoDbEntity<T>
    {
        ArgumentNullException.ThrowIfNull(name);

        // Through the schema of CreateTable, so that this table is the one the entity would have created, with types and the projection included.
        const string Entity = "The entity";
        var schema = new TableSchema(Entity, T.Info.PartitionKeyName, T.Info.SortKeyName);
        schema.Add(Entity, T.Info);
        var request = schema.ToRequest(name, null, null);
        return TableAnswer("Table", new TableDescription
        {
            TableName = name,
            TableStatus = status,
            AttributeDefinitions = request.AttributeDefinitions,
            KeySchema = request.KeySchema,
            LocalSecondaryIndexes = [.. request.LocalSecondaryIndexes!.Select(static index => new LocalSecondaryIndexDescription { IndexName = index.IndexName, KeySchema = index.KeySchema, Projection = index.Projection })],
            GlobalSecondaryIndexes =
            [
                .. request.GlobalSecondaryIndexes!.Select(static index => new GlobalSecondaryIndexDescription
                {
                    IndexName = index.IndexName,
                    IndexStatus = IndexStatus.Active,
                    KeySchema = index.KeySchema,
                    Projection = index.Projection,
                }),
            ],
        });
    }

    /// <summary>
    /// Creates a global index for <see cref="Table(string, TableStatus, ReadOnlySpan{GlobalSecondaryIndexDescription})"/>, projecting all attributes, with one string key attribute named after it, <c>ByCustomer_pk</c>. A wait reads the name and
    /// the status. It is a record, so a test says the rest with <c>with</c>: <c>GlobalIndex("ByCustomer", IndexStatus.Creating) with { Backfilling = true }</c>.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="status">The status of the index.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="indexName"/> is <see langword="null"/>.</exception>
    public static GlobalSecondaryIndexDescription GlobalIndex(string indexName, IndexStatus status = IndexStatus.Active)
    {
        ArgumentNullException.ThrowIfNull(indexName);
        return new GlobalSecondaryIndexDescription
        {
            IndexName = indexName,
            IndexStatus = status,
            KeySchema = [new KeySchemaElement(indexName + "_pk", KeyType.Hash)],
            Projection = new IndexProjection { ProjectionType = ProjectionType.All },
        };
    }

    /// <summary>
    /// Creates the answer to a <c>CreateTable</c>: the description of <see cref="Table(string, TableStatus, ReadOnlySpan{GlobalSecondaryIndexDescription})"/> under <c>TableDescription</c>, with the status <c>CREATING</c>.
    /// An emulator answers with a table that is <c>ACTIVE</c> already, for which a test passes that status.
    /// </summary>
    /// <param name="name">The table name.</param>
    /// <param name="status">The status of the table.</param>
    /// <param name="globalIndexes">The global secondary indexes, as <see cref="GlobalIndex"/> makes them. DynamoDB reports them as <c>CREATING</c> here.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> or the status of an index is <c>Unknown</c>, which has no name on the wire, or not a member of its enum.</exception>
    public static StubResponse TableCreated(string name, TableStatus status = TableStatus.Creating, params ReadOnlySpan<GlobalSecondaryIndexDescription> globalIndexes)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TableAnswer("TableDescription", MadeUpTable(name, status, globalIndexes));
    }

    /// <summary>
    /// Creates the answer to a <c>DeleteTable</c>: <c>TableDescription</c> with the status <c>DELETING</c> and only what the example of the API reference holds, because a table that is going away reports no keys and no indexes.
    /// </summary>
    /// <param name="name">The table name.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static StubResponse TableDeleted(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Json(Render(json =>
        {
            json.WriteStartObject("TableDescription");
            json.WriteString("TableName", name);
            WireNames.TableStatuses.Write(json, "TableStatus"u8, TableStatus.Deleting);
            WriteSizeAndIdentity(json, name);
            json.WriteEndObject();
        }));
    }

    /// <summary>Creates the answer to a <c>DescribeTimeToLive</c>: <c>{"TimeToLiveDescription":{"TimeToLiveStatus":"ENABLED","AttributeName":"expires"}}</c>.</summary>
    /// <param name="status">The status. A table that never had time to live is <see cref="TimeToLiveStatus.Disabled"/>.</param>
    /// <param name="attributeName">The attribute that holds the expiry, or <see langword="null"/> for none, which is what DynamoDB sends for a disabled table.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is <see cref="TimeToLiveStatus.Unknown"/>, which has no name on the wire, or not a member of the enum.</exception>
    public static StubResponse TimeToLive(TimeToLiveStatus status, string? attributeName = null) => Json(Render(json =>
    {
        json.WriteStartObject("TimeToLiveDescription");
        WireNames.TimeToLiveStatuses.Write(json, "TimeToLiveStatus"u8, status);
        OperationJson.WriteStringIfSet(json, "AttributeName"u8, attributeName);
        json.WriteEndObject();
    }));

    /// <summary>Creates the answer to an <c>UpdateTimeToLive</c>, which repeats the specification of the request: <c>{"TimeToLiveSpecification":{"AttributeName":"expires","Enabled":true}}</c>.</summary>
    /// <param name="enabled">Whether the request turned time to live on.</param>
    /// <param name="attributeName">The attribute that holds the expiry.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attributeName"/> is <see langword="null"/>.</exception>
    public static StubResponse TimeToLiveUpdated(bool enabled, string attributeName)
    {
        ArgumentNullException.ThrowIfNull(attributeName);
        return Json(Render(json =>
        {
            json.WriteStartObject("TimeToLiveSpecification");
            json.WriteString("AttributeName", attributeName);
            json.WriteBoolean("Enabled", enabled);
            json.WriteEndObject();
        }));
    }

    private static TableDescription MadeUpTable(string name, TableStatus status, ReadOnlySpan<GlobalSecondaryIndexDescription> globalIndexes)
    {
        var attributes = new List<AttributeDefinition> { new(DefaultKey, ScalarAttributeType.String) };
        foreach (var index in globalIndexes)
        {
            foreach (var element in index.KeySchema)
            {
                if (!attributes.Exists(attribute => attribute.AttributeName == element.AttributeName))
                {
                    attributes.Add(new AttributeDefinition(element.AttributeName, ScalarAttributeType.String));
                }
            }
        }

        return new TableDescription
        {
            TableName = name,
            TableStatus = status,
            AttributeDefinitions = attributes,
            KeySchema = [new KeySchemaElement(DefaultKey, KeyType.Hash)],
            GlobalSecondaryIndexes = globalIndexes.ToArray(),
        };
    }

    // The members in the order of the service model. What the description does not carry is the same for every table here, and the lists that are empty stay out, as they do on the wire.
    private static StubResponse TableAnswer(string member, TableDescription table) => Json(Render(json =>
    {
        var name = table.TableName!;
        json.WriteStartObject(member);
        TableJson.WriteAttributeDefinitions(json, table.AttributeDefinitions);
        json.WriteString("TableName", name);
        TableJson.WriteKeySchema(json, table.KeySchema);
        WireNames.TableStatuses.Write(json, "TableStatus"u8, table.TableStatus);
        json.WriteNumber("CreationDateTime", CreationEpochSeconds);
        WriteSizeAndIdentity(json, name);
        json.WriteStartObject("BillingModeSummary");
        WireNames.BillingModes.Write(json, "BillingMode"u8, BillingMode.PayPerRequest);
        json.WriteEndObject();
        WriteLocalIndexes(json, table.LocalSecondaryIndexes);
        WriteGlobalIndexes(json, name, table.GlobalSecondaryIndexes);
        json.WriteBoolean("DeletionProtectionEnabled", false);
        json.WriteEndObject();
    }));

    private static void WriteSizeAndIdentity(Utf8JsonWriter json, string tableName)
    {
        WriteNoCapacity(json);
        json.WriteNumber("TableSizeBytes", 0);
        json.WriteNumber("ItemCount", 0);
        json.WriteString("TableArn", ArnPrefix + tableName);
        json.WriteString("TableId", TableId);
    }

    // A table on demand reports zero, and a counter that is only there for provisioned capacity.
    private static void WriteNoCapacity(Utf8JsonWriter json)
    {
        json.WriteStartObject("ProvisionedThroughput");
        json.WriteNumber("NumberOfDecreasesToday", 0);
        json.WriteNumber("ReadCapacityUnits", 0);
        json.WriteNumber("WriteCapacityUnits", 0);
        json.WriteEndObject();
    }

    private static void WriteLocalIndexes(Utf8JsonWriter json, IReadOnlyList<LocalSecondaryIndexDescription> indexes)
    {
        if (indexes.Count == 0)
        {
            return;
        }

        json.WriteStartArray("LocalSecondaryIndexes");
        foreach (var index in indexes)
        {
            json.WriteStartObject();
            json.WriteString("IndexName", index.IndexName);
            TableJson.WriteKeySchema(json, index.KeySchema);
            TableJson.WriteProjection(json, index.Projection!);
            json.WriteNumber("IndexSizeBytes", 0);
            json.WriteNumber("ItemCount", 0);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteGlobalIndexes(Utf8JsonWriter json, string tableName, IReadOnlyList<GlobalSecondaryIndexDescription> indexes)
    {
        if (indexes.Count == 0)
        {
            return;
        }

        json.WriteStartArray("GlobalSecondaryIndexes");
        foreach (var index in indexes)
        {
            json.WriteStartObject();
            json.WriteString("IndexName", index.IndexName);
            TableJson.WriteKeySchema(json, index.KeySchema);
            if (index.Projection is { } projection)
            {
                TableJson.WriteProjection(json, projection);
            }

            WireNames.IndexStatuses.Write(json, "IndexStatus"u8, index.IndexStatus);
            OperationJson.WriteIfTrue(json, "Backfilling"u8, index.Backfilling);
            WriteNoCapacity(json);
            json.WriteNumber("IndexSizeBytes", 0);
            json.WriteNumber("ItemCount", 0);
            json.WriteString("IndexArn", ArnPrefix + tableName + "/index/" + index.IndexName);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}
