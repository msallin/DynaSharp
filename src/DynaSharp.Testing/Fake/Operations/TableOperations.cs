using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// <c>CreateTable</c>, <c>DescribeTable</c>, <c>UpdateTable</c>, <c>DeleteTable</c> and <c>ListTables</c>. A table and each of its indexes are <c>ACTIVE</c> in the answer that
/// creates them, a new index holds the items of the table at once, and a deleted table frees its name at once.
/// </summary>
/// <remarks>
/// <para>
/// The wire reads a body into the records of the request API, <see cref="CreateTableRequest"/> and <see cref="UpdateTableRequest"/>, and <see cref="Create"/> and <see cref="Update"/> check
/// and apply them, so a table made with <see cref="FakeDynamoDb.CreateTable(CreateTableRequest)"/> goes through the same checks as one a client creates. Lists that are empty are
/// none in a record, as the client writes them; on the wire DynamoDB rejects an empty list of indexes, and so does the reader here.
/// </para>
/// <para>
/// Provisioned throughput is kept and reported, never enforced. Tags are not part of the fake and are <c>UnsupportedByFakeException</c>.
/// </para>
/// <para>
/// An <c>SSESpecification</c> with <c>Enabled: true</c> in <c>CreateTable</c> encrypts the table with a KMS key, in name only: the description reports
/// <c>SSEDescription</c> with <c>ENABLED</c>, <c>KMS</c> and the ARN of the key. Without <c>KMSMasterKeyId</c> that is <see cref="FakeTable.AwsManagedKeyArn"/>; an ARN is
/// echoed, and a key id or an alias name becomes an ARN of the fake's region and account (DynamoDB reports the key behind an alias, which the fake cannot know).
/// <c>SSEType</c> is KMS whatever the request says, the only type DynamoDB documents for a request. <c>Enabled: false</c> or no specification is the key that DynamoDB owns,
/// which has no <c>SSEDescription</c>. <c>UpdateTable</c> does not change the encryption.
/// </para>
/// <para>
/// A <c>StreamSpecification</c> with <c>StreamEnabled: true</c> enables a new <see cref="FakeStream"/>, with a new ARN and label each time, and <c>false</c> in <c>UpdateTable</c>
/// disables it, which closes its shard; so does <c>DeleteTable</c>. The description names the stream enabled last in <c>LatestStreamArn</c> and <c>LatestStreamLabel</c>, also after
/// it was disabled, and carries <c>StreamSpecification</c> while it is enabled.
/// </para>
/// </remarks>
internal static class TableOperations
{
    /// <summary>The operation.</summary>
    public const string CreateTableName = "CreateTable";

    /// <summary>The operation.</summary>
    public const string DescribeTableName = "DescribeTable";

    /// <summary>The operation.</summary>
    public const string DeleteTableName = "DeleteTable";

    /// <summary>The operation.</summary>
    public const string UpdateTableName = "UpdateTable";

    /// <summary>The operation.</summary>
    public const string ListTablesName = "ListTables";

    private const int MaxLocalIndexes = 5;
    private const int MaxGlobalIndexes = 20;
    private const int MaxListLimit = 100;
    private const string Invalid = "One or more parameter values were invalid: ";

    /// <summary>Answers <c>CreateTable</c>.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleCreateTable(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        var table = Create(db, ReadCreateTable(request));
        ResponseJson.WriteTableDescription(response, "TableDescription", table, TableStatus.Active);
    }

    /// <summary>Answers <c>DescribeTable</c>.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleDescribeTable(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, DescribeTableName, "TableName");
        ResponseJson.WriteTableDescription(response, "Table", db.DescribeTable(ReadTableName(request)), TableStatus.Active);
    }

    /// <summary>Answers <c>DeleteTable</c>.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleDeleteTable(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, DeleteTableName, "TableName");
        ResponseJson.WriteTableDescription(response, "TableDescription", Delete(db, ReadTableName(request)), TableStatus.Deleting);
    }

    /// <summary>Answers <c>UpdateTable</c>.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleUpdateTable(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        var table = Update(db, ReadUpdateTable(request));
        ResponseJson.WriteTableDescription(response, "TableDescription", table, TableStatus.Active);
    }

    /// <summary>Answers <c>ListTables</c>: the names in ordinal order after <c>ExclusiveStartTableName</c>, at most <c>Limit</c> of them, and the last one as <c>LastEvaluatedTableName</c> when more follow.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleListTables(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, ListTablesName, "ExclusiveStartTableName", "Limit");
        var after = RequestJson.Optional(request, "ExclusiveStartTableName") is { } start ? RequestJson.ReadString(start, "ExclusiveStartTableName") : null;
        var limit = RequestJson.Optional(request, "Limit") is { } value ? RequestJson.ReadInt(value, "Limit") : MaxListLimit;
        if (limit is < 1 or > MaxListLimit)
        {
            throw FakeError.Validation($"1 validation error detected: Value '{limit}' at 'limit' failed to satisfy constraint: Member must have value {(limit < 1 ? "greater than or equal to 1" : "less than or equal to 100")}");
        }

        var names = db.Tables.Keys.Where(name => after is null || string.CompareOrdinal(name, after) > 0).ToList();
        var page = names.Take(limit).ToList();
        Write(response, page, names.Count > page.Count ? page[^1] : null);

        static void Write(Utf8JsonWriter response, List<string> page, string? last)
        {
            response.WriteStartArray("TableNames"u8);
            foreach (var name in page)
            {
                response.WriteStringValue(name);
            }

            response.WriteEndArray();
            OperationJson.WriteStringIfSet(response, "LastEvaluatedTableName"u8, last);
        }
    }

    /// <summary>Checks a request to create a table and creates it.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The request.</param>
    /// <returns>The new table, <c>ACTIVE</c>.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>, a <c>ResourceInUseException</c> for a name that is taken, an <c>UnsupportedByFakeException</c>.</exception>
    public static FakeTable Create(FakeDatabase db, CreateTableRequest request)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalidName(request.TableName, "tableName");
        if (request.Tags is { Count: > 0 })
        {
            throw FakeError.Unsupported("the member Tags", CreateTableName);
        }

        if (request.StreamSpecification is { } stream)
        {
            ThrowIfInvalidStreamSpecification(stream);
        }

        var types = Types(request.AttributeDefinitions);
        var table = new FakeTable(request.TableName, ToKeySchema(request.KeySchema, types), request.AttributeDefinitions, db.Clock.GetUtcNow())
        {
            BillingMode = request.BillingMode == BillingMode.Unspecified ? BillingMode.Provisioned : request.BillingMode,
            TableClass = request.TableClass,
            DeletionProtectionEnabled = request.DeletionProtectionEnabled,
            KmsMasterKeyArn = KmsKeyArn(request.SSESpecification),
        };
        var locals = request.LocalSecondaryIndexes ?? [];
        var globals = request.GlobalSecondaryIndexes ?? [];
        if (locals.Count > MaxLocalIndexes)
        {
            throw FakeError.Validation(Invalid + $"Number of LocalSecondaryIndexes exceeds per-table limit of {MaxLocalIndexes}");
        }

        if (globals.Count > MaxGlobalIndexes)
        {
            throw FakeError.Validation(Invalid + $"Number of GlobalSecondaryIndexes exceeds per-table limit of {MaxGlobalIndexes}");
        }

        foreach (var local in locals)
        {
            AddLocalIndex(table, local, types);
        }

        foreach (var global in globals)
        {
            table.GlobalIndexes.Add(NewGlobalIndex(table, global.IndexName, global.KeySchema, global.Projection, types));
        }

        (table.ReadCapacityUnits, table.WriteCapacityUnits) = Throughput(table.BillingMode, request.ProvisionedThroughput, null);
        for (var i = 0; i < globals.Count; i++)
        {
            (table.GlobalIndexes[i].ReadCapacityUnits, table.GlobalIndexes[i].WriteCapacityUnits) = Throughput(table.BillingMode, globals[i].ProvisionedThroughput, globals[i].IndexName);
        }

        ThrowIfUnusedDefinitions(table, types);
        db.Create(table);
        if (request.StreamSpecification is { StreamEnabled: true } enabled)
        {
            db.EnableStream(table, enabled.StreamViewType);
        }

        return table;
    }

    /// <summary>Checks a request to update a table and applies it.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The request.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>, a <c>ResourceNotFoundException</c>, a <c>LimitExceededException</c>, an <c>UnsupportedByFakeException</c>.</exception>
    public static FakeTable Update(FakeDatabase db, UpdateTableRequest request)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalidName(request.TableName, "tableName");
        var table = db.DescribeTable(request.TableName);
        var updates = request.GlobalSecondaryIndexUpdates ?? [];
        if (request.BillingMode == BillingMode.Unspecified && request.ProvisionedThroughput is null && updates.Count == 0
            && request.TableClass == TableClass.Unspecified && request.DeletionProtectionEnabled is null && request.StreamSpecification is null)
        {
            throw FakeError.Validation("At least one of ProvisionedThroughput, BillingMode, UpdateStreamEnabled, GlobalSecondaryIndexUpdates or SSESpecification or ReplicaUpdates is required");
        }

        if (updates.Count(static update => update.Create is not null || update.Delete is not null) > 1)
        {
            throw FakeError.LimitExceeded("Subscriber limit exceeded: Only 1 online index can be created or deleted simultaneously per table");
        }

        var billingMode = request.BillingMode == BillingMode.Unspecified ? table.BillingMode : request.BillingMode;
        var types = Types(table.AttributeDefinitions.Concat(request.AttributeDefinitions ?? []).DistinctBy(static definition => definition.AttributeName));

        // DynamoDB resolves the key attributes of a new index against the request's AttributeDefinitions only, not against the ones the table stores.
        var declared = Types(request.AttributeDefinitions ?? []);
        var (read, write) = request.ProvisionedThroughput is null && billingMode == table.BillingMode
            ? (table.ReadCapacityUnits, table.WriteCapacityUnits)
            : Throughput(billingMode, request.ProvisionedThroughput, null);
        if (request.ProvisionedThroughput is not null && billingMode == table.BillingMode && read == table.ReadCapacityUnits && write == table.WriteCapacityUnits)
        {
            throw FakeError.Validation("The provisioned throughput for the table will not change. The requested value equals the current value. Current ReadCapacityUnits provisioned for the table: "
                + $"{read}. Requested ReadCapacityUnits: {read}. Current WriteCapacityUnits provisioned for the table: {write}. Requested WriteCapacityUnits: {write}. Refer to the Amazon DynamoDB Developer Guide for current limits and how to request higher limits.");
        }

        ThrowIfInvalidStreamChange(table, request.StreamSpecification);

        // Everything is checked on copies first, so that a request that fails leaves the table as it was.
        var indexes = table.GlobalIndexes.ToList();
        var throughput = indexes.ToDictionary(static index => index.Name, static index => (index.ReadCapacityUnits, index.WriteCapacityUnits), StringComparer.Ordinal);
        FakeIndex? created = null;
        foreach (var update in updates)
        {
            if (update.Create is { } create)
            {
                if (table.FindIndex(create.IndexName) is not null)
                {
                    throw FakeError.Validation(Invalid + $"Attempting to create an index which already exists: {create.IndexName}");
                }

                created = NewGlobalIndex(table, create.IndexName, create.KeySchema, create.Projection, declared);
                indexes.Add(created);
                throughput[create.IndexName] = Throughput(billingMode, create.ProvisionedThroughput, create.IndexName);
            }
            else if (update.Update is { } change)
            {
                _ = Existing(indexes, change.IndexName);
                throughput[change.IndexName] = Throughput(billingMode, change.ProvisionedThroughput, change.IndexName);
            }
            else if (update.Delete is { } delete)
            {
                indexes.Remove(Existing(indexes, delete.IndexName));
            }
            else
            {
                throw FakeError.Validation(Invalid + "A GlobalSecondaryIndexUpdate must have one of Create, Update or Delete");
            }
        }

        if (billingMode != table.BillingMode)
        {
            foreach (var index in indexes.Where(index => index != created && !updates.Any(update => update.Update?.IndexName == index.Name)))
            {
                throughput[index.Name] = Throughput(billingMode, null, index.Name);
            }
        }

        table.BillingMode = billingMode;
        (table.ReadCapacityUnits, table.WriteCapacityUnits) = (read, write);
        table.GlobalIndexes.Clear();
        table.GlobalIndexes.AddRange(indexes);
        foreach (var index in indexes)
        {
            (index.ReadCapacityUnits, index.WriteCapacityUnits) = throughput[index.Name];
        }

        created?.Backfill(table.All());
        table.AttributeDefinitions = [.. types.Where(type => KeyNames(table).Contains(type.Key)).Select(static type => new AttributeDefinition(type.Key, type.Value))];
        table.TableClass = request.TableClass == TableClass.Unspecified ? table.TableClass : request.TableClass;
        table.DeletionProtectionEnabled = request.DeletionProtectionEnabled ?? table.DeletionProtectionEnabled;
        if (request.StreamSpecification is { } stream)
        {
            ChangeStream(db, table, stream);
        }

        return table;
    }

    /// <summary>Deletes a table, unless it is protected against deletion.</summary>
    /// <param name="db">The state.</param>
    /// <param name="name">The table.</param>
    /// <returns>The table that is gone.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>, a <c>ValidationException</c>.</exception>
    public static FakeTable Delete(FakeDatabase db, string name)
    {
        ArgumentNullException.ThrowIfNull(db);
        ThrowIfInvalidName(name, "tableName");
        if (db.DescribeTable(name).DeletionProtectionEnabled)
        {
            throw FakeError.Validation("Resource cannot be deleted as it is currently protected against deletion. Disable deletion protection first.");
        }

        var table = db.Delete(name);
        table.LatestStream?.Close(db.Clock.GetUtcNow());
        return table;
    }

    /// <summary>Checks the name of a table or an index: 3 to 255 characters of <c>a-z</c>, <c>A-Z</c>, <c>0-9</c>, <c>_</c>, <c>-</c> and <c>.</c>.</summary>
    /// <param name="name">The name.</param>
    /// <param name="member">The member in camel case, for the message: <c>tableName</c>, <c>indexName</c>.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static void ThrowIfInvalidName(string name, string member)
    {
        ArgumentNullException.ThrowIfNull(name);
        var problem = name.Length switch
        {
            < 3 => "Member must have length greater than or equal to 3",
            > 255 => "Member must have length less than or equal to 255",
            _ => name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? null : "Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+",
        };
        if (problem is not null)
        {
            throw FakeError.Validation($"1 validation error detected: Value '{name}' at '{member}' failed to satisfy constraint: {problem}");
        }
    }

    /// <summary>Reads <c>TableName</c>, checked as DynamoDB checks it.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The name.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a missing or malformed name.</exception>
    public static string ReadTableName(JsonElement request)
    {
        var name = RequestJson.ReadString(RequestJson.Required(request, "TableName"), "TableName");
        ThrowIfInvalidName(name, "tableName");
        return name;
    }

    private static string? KmsKeyArn(SSESpecification? specification) => specification switch
    {
        { Enabled: false } or null => null,
        { KMSMasterKeyId: null } => FakeTable.AwsManagedKeyArn,
        { KMSMasterKeyId: { } id } when id.StartsWith("arn:", StringComparison.Ordinal) => id,
        { KMSMasterKeyId: { } id } when id.StartsWith("alias/", StringComparison.Ordinal) => FakeTable.KmsArnPrefix + id,
        { KMSMasterKeyId: { } id } => FakeTable.KmsArnPrefix + "key/" + id,
    };

    private static void ThrowIfInvalidStreamSpecification(StreamSpecification stream)
    {
        if (stream.StreamEnabled && stream.StreamViewType == StreamViewType.Unspecified)
        {
            throw FakeError.Validation(Invalid + "StreamViewType must be specified when StreamEnabled is true");
        }

        if (!stream.StreamEnabled && stream.StreamViewType != StreamViewType.Unspecified)
        {
            throw FakeError.Validation("Invalid StreamViewType: Stream StreamEnabled was false but a StreamViewType was present.");
        }
    }

    // DynamoDB enables a stream only on a table without an enabled one, and disables only an enabled one: a new view type takes both, one request after the other.
    private static void ThrowIfInvalidStreamChange(FakeTable table, StreamSpecification? stream)
    {
        if (stream is null)
        {
            return;
        }

        ThrowIfInvalidStreamSpecification(stream);
        var enabled = table.LatestStream is { IsEnabled: true };
        if (stream.StreamEnabled && enabled)
        {
            throw FakeError.Validation($"Table already has an enabled stream: TableName: {table.Name}");
        }

        if (!stream.StreamEnabled && !enabled)
        {
            throw FakeError.Validation("Table has no stream to disable");
        }
    }

    private static void ChangeStream(FakeDatabase db, FakeTable table, StreamSpecification stream)
    {
        if (stream.StreamEnabled)
        {
            db.EnableStream(table, stream.StreamViewType);
        }
        else
        {
            table.LatestStream!.Close(db.Clock.GetUtcNow());
        }
    }

    private static FakeIndex Existing(List<FakeIndex> indexes, string name) =>
        indexes.Find(index => index.Name == name) ?? throw FakeError.ResourceNotFound($"Requested resource not found: Index: {name} not found");

    private static HashSet<string> KeyNames(FakeTable table) =>
        [.. table.KeySchema.Names.Concat(table.LocalIndexes.Concat(table.GlobalIndexes).SelectMany(static index => index.KeySchema.Names))];

    private static Dictionary<string, ScalarAttributeType> Types(IEnumerable<AttributeDefinition> definitions)
    {
        var types = new Dictionary<string, ScalarAttributeType>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition.AttributeType == ScalarAttributeType.Unknown)
            {
                throw FakeError.Validation(Invalid + $"The AttributeType of {definition.AttributeName} must be S, N or B");
            }

            if (!types.TryAdd(definition.AttributeName, definition.AttributeType))
            {
                throw FakeError.Validation("Cannot have two attributes with the same name");
            }
        }

        return types;
    }

    private static void ThrowIfUnusedDefinitions(FakeTable table, Dictionary<string, ScalarAttributeType> types)
    {
        var used = KeyNames(table);
        if (types.Keys.Any(name => !used.Contains(name)))
        {
            throw FakeError.Validation(Invalid + "Number of attributes in KeySchema does not exactly match number of attributes defined in AttributeDefinitions");
        }
    }

    private static KeySchema ToKeySchema(IReadOnlyList<KeySchemaElement> elements, Dictionary<string, ScalarAttributeType> types)
    {
        if (elements.Count is < 1 or > 2)
        {
            throw FakeError.Validation($"1 validation error detected: Value at 'keySchema' failed to satisfy constraint: Member must have length {(elements.Count < 1 ? "greater than or equal to 1" : "less than or equal to 2")}");
        }

        if (elements[0].KeyType != KeyType.Hash)
        {
            throw FakeError.Validation("Invalid KeySchema: The first KeySchemaElement is not a HASH key type");
        }

        if (elements.Count == 2 && elements[1].KeyType != KeyType.Range)
        {
            throw FakeError.Validation("Invalid KeySchema: The second KeySchemaElement is not a RANGE key type");
        }

        if (elements.Count == 2 && elements[0].AttributeName == elements[1].AttributeName)
        {
            throw FakeError.Validation("Both the Hash Key and the Range Key element in the KeySchema have the same name");
        }

        var missing = elements.Where(element => !types.ContainsKey(element.AttributeName)).ToList();
        if (missing.Count > 0)
        {
            throw FakeError.Validation(Invalid + $"Some index key attributes are not defined in AttributeDefinitions. Keys: [{string.Join(", ", elements.Select(static element => element.AttributeName))}], "
                + $"AttributeDefinitions: [{string.Join(", ", types.Keys)}]");
        }

        var partition = elements[0].AttributeName;
        return elements.Count == 1
            ? new KeySchema(partition, types[partition])
            : new KeySchema(partition, types[partition], elements[1].AttributeName, types[elements[1].AttributeName]);
    }

    private static void AddLocalIndex(FakeTable table, LocalSecondaryIndex local, Dictionary<string, ScalarAttributeType> types)
    {
        if (table.KeySchema.SortKeyName is null)
        {
            throw FakeError.Validation(Invalid + "Table KeySchema does not have a range key, which is required when specifying a LocalSecondaryIndex");
        }

        ThrowIfInvalidIndexName(table, local.IndexName);
        var keySchema = ToKeySchema(local.KeySchema, types);
        if (keySchema.SortKeyName is null || keySchema.PartitionKeyName != table.KeySchema.PartitionKeyName)
        {
            throw FakeError.Validation(Invalid + $"Index KeySchema does not have the same leading hash key as table KeySchema for index: {local.IndexName}. "
                + $"index hash key: {keySchema.PartitionKeyName}, table hash key: {table.KeySchema.PartitionKeyName}");
        }

        table.LocalIndexes.Add(new FakeIndex(local.IndexName, keySchema, CheckedProjection(local.Projection), isGlobal: false, table.KeySchema));
    }

    private static FakeIndex NewGlobalIndex(FakeTable table, string name, IReadOnlyList<KeySchemaElement> keySchema, IndexProjection projection, Dictionary<string, ScalarAttributeType> types)
    {
        ThrowIfInvalidIndexName(table, name);
        return new FakeIndex(name, ToKeySchema(keySchema, types), CheckedProjection(projection), isGlobal: true, table.KeySchema);
    }

    private static void ThrowIfInvalidIndexName(FakeTable table, string name)
    {
        ThrowIfInvalidName(name, "indexName");
        if (table.FindIndex(name) is not null)
        {
            throw FakeError.Validation(Invalid + $"Duplicate index name: {name}");
        }
    }

    private static IndexProjection CheckedProjection(IndexProjection projection)
    {
        if (!WireNames.ProjectionTypes.TryGetName(projection.ProjectionType, out var type))
        {
            throw FakeError.Validation(Invalid + "The ProjectionType of an index must be ALL, KEYS_ONLY or INCLUDE");
        }

        var hasNonKeyAttributes = projection.NonKeyAttributes is { Count: > 0 };
        if (projection.ProjectionType == ProjectionType.Include && !hasNonKeyAttributes)
        {
            throw FakeError.Validation(Invalid + "ProjectionType is INCLUDE, but NonKeyAttributes is not specified");
        }

        if (projection.ProjectionType != ProjectionType.Include && hasNonKeyAttributes)
        {
            throw FakeError.Validation(Invalid + $"ProjectionType is {type}, but NonKeyAttributes is specified");
        }

        return projection;
    }

    // What a table or an index is provisioned with: the numbers for PROVISIONED, which must be given and at least 1, and zero on demand, where none may be given.
    private static (long Read, long Write) Throughput(BillingMode mode, ProvisionedThroughput? throughput, string? indexName)
    {
        if (mode == BillingMode.PayPerRequest)
        {
            return throughput is null
                ? (0, 0)
                : throw FakeError.Validation(Invalid + (indexName is null
                    ? "Neither ReadCapacityUnits nor WriteCapacityUnits can be specified when BillingMode is PAY_PER_REQUEST"
                    : $"ProvisionedThroughput should not be specified for index: {indexName} when BillingMode is PAY_PER_REQUEST"));
        }

        if (throughput is null)
        {
            throw FakeError.Validation(Invalid + (indexName is null
                ? "ReadCapacityUnits and WriteCapacityUnits must both be specified when BillingMode is PROVISIONED"
                : $"ProvisionedThroughput must be specified for index: {indexName}"));
        }

        foreach (var (value, member) in new[] { (throughput.ReadCapacityUnits, "readCapacityUnits"), (throughput.WriteCapacityUnits, "writeCapacityUnits") })
        {
            if (value < 1)
            {
                throw FakeError.Validation($"1 validation error detected: Value '{value}' at 'provisionedThroughput.{member}' failed to satisfy constraint: Member must have value greater than or equal to 1");
            }
        }

        return (throughput.ReadCapacityUnits, throughput.WriteCapacityUnits);
    }

    private static CreateTableRequest ReadCreateTable(JsonElement request)
    {
        RequestJson.Members(
            request,
            CreateTableName,
            "AttributeDefinitions",
            "TableName",
            "KeySchema",
            "LocalSecondaryIndexes",
            "GlobalSecondaryIndexes",
            "BillingMode",
            "ProvisionedThroughput",
            "StreamSpecification",
            "SSESpecification",
            "TableClass",
            "DeletionProtectionEnabled");
        return new CreateTableRequest
        {
            AttributeDefinitions = ReadDefinitions(RequestJson.Required(request, "AttributeDefinitions"), CreateTableName),
            TableName = ReadTableName(request),
            KeySchema = ReadKeyElements(RequestJson.Required(request, "KeySchema"), CreateTableName),
            LocalSecondaryIndexes = ReadIndexes(request, "LocalSecondaryIndexes", ReadLocalIndex),
            GlobalSecondaryIndexes = ReadIndexes(request, "GlobalSecondaryIndexes", element => ReadGlobalIndex(element, "GlobalSecondaryIndexes of CreateTable")),
            BillingMode = ReadOptionalEnum(request, "BillingMode", WireNames.BillingModes),
            ProvisionedThroughput = ReadOptionalThroughput(request, "ProvisionedThroughput", CreateTableName),
            StreamSpecification = RequestJson.Optional(request, "StreamSpecification") is { } stream ? ReadStream(stream, CreateTableName) : null,
            SSESpecification = RequestJson.Optional(request, "SSESpecification") is { } sse ? ReadSse(sse) : null,
            TableClass = ReadOptionalEnum(request, "TableClass", WireNames.TableClasses),
            DeletionProtectionEnabled = RequestJson.Optional(request, "DeletionProtectionEnabled") is { } protection && RequestJson.ReadBool(protection, "DeletionProtectionEnabled"),
        };
    }

    private static UpdateTableRequest ReadUpdateTable(JsonElement request)
    {
        RequestJson.Members(
            request,
            UpdateTableName,
            "AttributeDefinitions",
            "TableName",
            "BillingMode",
            "ProvisionedThroughput",
            "GlobalSecondaryIndexUpdates",
            "StreamSpecification",
            "TableClass",
            "DeletionProtectionEnabled");
        return new UpdateTableRequest
        {
            AttributeDefinitions = RequestJson.Optional(request, "AttributeDefinitions") is { } definitions ? ReadDefinitions(definitions, UpdateTableName) : null,
            TableName = ReadTableName(request),
            BillingMode = ReadOptionalEnum(request, "BillingMode", WireNames.BillingModes),
            ProvisionedThroughput = ReadOptionalThroughput(request, "ProvisionedThroughput", UpdateTableName),
            GlobalSecondaryIndexUpdates = RequestJson.Optional(request, "GlobalSecondaryIndexUpdates") is { } updates
                ? [.. RequestJson.ReadArray(updates, "GlobalSecondaryIndexUpdates").Select(ReadIndexUpdate)]
                : null,
            StreamSpecification = RequestJson.Optional(request, "StreamSpecification") is { } stream ? ReadStream(stream, UpdateTableName) : null,
            TableClass = ReadOptionalEnum(request, "TableClass", WireNames.TableClasses),
            DeletionProtectionEnabled = RequestJson.Optional(request, "DeletionProtectionEnabled") is { } protection ? RequestJson.ReadBool(protection, "DeletionProtectionEnabled") : null,
        };
    }

    private static GlobalSecondaryIndexUpdate ReadIndexUpdate(JsonElement element)
    {
        const string Where = "GlobalSecondaryIndexUpdates of UpdateTable";
        RequestJson.Members(element, Where, "Create", "Update", "Delete");
        if (element.EnumerateObject().Count() != 1)
        {
            throw FakeError.Validation(Invalid + "A GlobalSecondaryIndexUpdate must have one of Create, Update or Delete");
        }

        if (RequestJson.Optional(element, "Create") is { } create)
        {
            return new GlobalSecondaryIndexUpdate { Create = ReadGlobalIndex(create, "Create of UpdateTable") };
        }

        if (RequestJson.Optional(element, "Update") is { } update)
        {
            RequestJson.Members(update, "Update of UpdateTable", "IndexName", "ProvisionedThroughput");
            return new GlobalSecondaryIndexUpdate
            {
                Update = new UpdateGlobalSecondaryIndexAction
                {
                    IndexName = RequestJson.ReadString(RequestJson.Required(update, "IndexName"), "IndexName"),
                    ProvisionedThroughput = ReadThroughput(RequestJson.Required(update, "ProvisionedThroughput"), "Update of UpdateTable"),
                },
            };
        }

        var delete = RequestJson.Required(element, "Delete");
        RequestJson.Members(delete, "Delete of UpdateTable", "IndexName");
        return new GlobalSecondaryIndexUpdate { Delete = new DeleteGlobalSecondaryIndexAction { IndexName = RequestJson.ReadString(RequestJson.Required(delete, "IndexName"), "IndexName") } };
    }

    private static List<T>? ReadIndexes<T>(JsonElement request, string member, Func<JsonElement, T> read)
    {
        if (RequestJson.Optional(request, member) is not { } value)
        {
            return null;
        }

        var indexes = RequestJson.ReadArray(value, member);
        return indexes.Count == 0 ? throw FakeError.Validation(Invalid + $"List of {member} is empty") : [.. indexes.Select(read)];
    }

    private static LocalSecondaryIndex ReadLocalIndex(JsonElement element)
    {
        const string Where = "LocalSecondaryIndexes of CreateTable";
        RequestJson.Members(element, Where, "IndexName", "KeySchema", "Projection");
        return new LocalSecondaryIndex
        {
            IndexName = RequestJson.ReadString(RequestJson.Required(element, "IndexName"), "IndexName"),
            KeySchema = ReadKeyElements(RequestJson.Required(element, "KeySchema"), Where),
            Projection = ReadProjection(RequestJson.Required(element, "Projection"), Where),
        };
    }

    private static GlobalSecondaryIndex ReadGlobalIndex(JsonElement element, string where)
    {
        RequestJson.Members(element, where, "IndexName", "KeySchema", "Projection", "ProvisionedThroughput");
        return new GlobalSecondaryIndex
        {
            IndexName = RequestJson.ReadString(RequestJson.Required(element, "IndexName"), "IndexName"),
            KeySchema = ReadKeyElements(RequestJson.Required(element, "KeySchema"), where),
            Projection = ReadProjection(RequestJson.Required(element, "Projection"), where),
            ProvisionedThroughput = ReadOptionalThroughput(element, "ProvisionedThroughput", where),
        };
    }

    private static List<AttributeDefinition> ReadDefinitions(JsonElement value, string operation) =>
    [
        .. RequestJson.ReadArray(value, "AttributeDefinitions").Select(element =>
        {
            RequestJson.Members(element, "AttributeDefinitions of " + operation, "AttributeName", "AttributeType");
            return new AttributeDefinition(
                RequestJson.ReadString(RequestJson.Required(element, "AttributeName"), "AttributeName"),
                RequestJson.ReadEnum(RequestJson.Required(element, "AttributeType"), "AttributeType", WireNames.ScalarAttributeTypes));
        }),
    ];

    private static List<KeySchemaElement> ReadKeyElements(JsonElement value, string where) =>
    [
        .. RequestJson.ReadArray(value, "KeySchema").Select(element =>
        {
            RequestJson.Members(element, "KeySchema of " + where, "AttributeName", "KeyType");
            return new KeySchemaElement(
                RequestJson.ReadString(RequestJson.Required(element, "AttributeName"), "AttributeName"),
                RequestJson.ReadEnum(RequestJson.Required(element, "KeyType"), "KeyType", WireNames.KeyTypes));
        }),
    ];

    private static IndexProjection ReadProjection(JsonElement value, string where)
    {
        RequestJson.Members(value, "Projection of " + where, "ProjectionType", "NonKeyAttributes");
        return new IndexProjection
        {
            ProjectionType = ReadOptionalEnum(value, "ProjectionType", WireNames.ProjectionTypes),
            NonKeyAttributes = RequestJson.Optional(value, "NonKeyAttributes") is { } names ? RequestJson.ReadStringList(names, "NonKeyAttributes") : null,
        };
    }

    private static ProvisionedThroughput? ReadOptionalThroughput(JsonElement value, string member, string where) =>
        RequestJson.Optional(value, member) is { } throughput ? ReadThroughput(throughput, where) : null;

    private static ProvisionedThroughput ReadThroughput(JsonElement value, string where)
    {
        RequestJson.Members(value, "ProvisionedThroughput of " + where, "ReadCapacityUnits", "WriteCapacityUnits");
        return new ProvisionedThroughput
        {
            ReadCapacityUnits = RequestJson.ReadLong(RequestJson.Required(value, "ReadCapacityUnits"), "ReadCapacityUnits"),
            WriteCapacityUnits = RequestJson.ReadLong(RequestJson.Required(value, "WriteCapacityUnits"), "WriteCapacityUnits"),
        };
    }

    private static StreamSpecification ReadStream(JsonElement value, string operation)
    {
        RequestJson.Members(value, "StreamSpecification of " + operation, "StreamEnabled", "StreamViewType");
        return new StreamSpecification
        {
            StreamEnabled = RequestJson.ReadBool(RequestJson.Required(value, "StreamEnabled"), "StreamEnabled"),
            StreamViewType = ReadOptionalEnum(value, "StreamViewType", WireNames.StreamViewTypes),
        };
    }

    private static SSESpecification ReadSse(JsonElement value)
    {
        RequestJson.Members(value, "SSESpecification of CreateTable", "Enabled", "SSEType", "KMSMasterKeyId");
        return new SSESpecification
        {
            Enabled = RequestJson.Optional(value, "Enabled") is { } enabled && RequestJson.ReadBool(enabled, "Enabled"),
            SSEType = ReadOptionalEnum(value, "SSEType", WireNames.SSETypes),
            KMSMasterKeyId = RequestJson.Optional(value, "KMSMasterKeyId") is { } key ? RequestJson.ReadString(key, "KMSMasterKeyId") : null,
        };
    }

    private static TEnum ReadOptionalEnum<TEnum>(JsonElement value, string member, EnumWireNames<TEnum> names)
        where TEnum : struct, Enum =>
        RequestJson.Optional(value, member) is { } found ? RequestJson.ReadEnum(found, member, names) : default;
}
