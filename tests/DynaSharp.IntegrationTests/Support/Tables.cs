using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// Creates and deletes the tables of the tests through the client's CreateTable and DeleteTable. This is the one place that knows
/// how a test table is made. Raw HTTP stays available through the emulator for tests that need the unmapped response.
/// </summary>
internal static class Tables
{
    /// <summary>The name of the global secondary index of a table made with <see cref="TableSpec.WithGlobalIndex"/>.</summary>
    public const string GlobalIndexName = "gsi";

    /// <summary>Creates a name that no other test uses.</summary>
    /// <returns>The table name.</returns>
    public static string UniqueName() => $"t{Guid.NewGuid():N}";

    /// <summary>Creates a table with a unique name and waits until it is active.</summary>
    /// <param name="client">The client of the emulator.</param>
    /// <param name="spec">The shape of the table.</param>
    /// <returns>The name of the table.</returns>
    public static async Task<string> CreateAsync(DynamoDbClient client, TableSpec spec)
    {
        var name = UniqueName();
        await CreateAsync(client, Request(name, spec));
        return name;
    }

    /// <summary>Creates the table of <paramref name="request"/> and waits until it is active.</summary>
    /// <param name="client">The client of the emulator.</param>
    /// <param name="request">The table to create.</param>
    /// <returns>The response of CreateTable.</returns>
    public static async Task<CreateTableResponse> CreateAsync(DynamoDbClient client, CreateTableRequest request)
    {
        var response = await CreateWithRetryAsync(client, request);
        await WaitUntilActiveAsync(client, request.TableName);
        return response;
    }

    /// <summary>Describes the table until <paramref name="condition"/> holds for it, for the changes that UpdateTable makes asynchronously.</summary>
    /// <param name="client">The client of the emulator.</param>
    /// <param name="name">The name of the table.</param>
    /// <param name="condition">What the description must show.</param>
    /// <returns>The description for which the condition held.</returns>
    /// <exception cref="OperationCanceledException">The condition did not hold within a minute.</exception>
    public static async Task<TableDescription> WaitUntilAsync(DynamoDbClient client, string name, Func<TableDescription, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        while (true)
        {
            var description = (await client.DescribeTableAsync(new DescribeTableRequest { TableName = name }, timeout.Token)).Table;
            if (condition(description))
            {
                return description;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }

    /// <summary>Tells whether a table and all its global secondary indexes are active, which they are not while UpdateTable works.</summary>
    /// <param name="description">The description of the table.</param>
    /// <returns><see langword="true"/> if nothing is being created, updated or deleted.</returns>
    public static bool IsSettled(TableDescription description) =>
        description.TableStatus == TableStatus.Active && description.GlobalSecondaryIndexes.All(static index => index.IndexStatus == IndexStatus.Active);

    /// <summary>Deletes a table.</summary>
    /// <param name="client">The client of the emulator.</param>
    /// <param name="name">The name of the table.</param>
    /// <returns>A task that completes when the emulator accepted the deletion.</returns>
    public static async Task DeleteAsync(DynamoDbClient client, string name) =>
        await client.DeleteTableAsync(new DeleteTableRequest { TableName = name });

    /// <summary>Builds the CreateTable request for a shape.</summary>
    /// <param name="name">The name of the table.</param>
    /// <param name="spec">The shape of the table.</param>
    /// <returns>The request.</returns>
    public static CreateTableRequest Request(string name, TableSpec spec)
    {
        var attributes = new List<AttributeDefinition> { new("pk", ScalarType(spec.PartitionKeyType)) };
        var keySchema = new List<KeySchemaElement> { new("pk", KeyType.Hash) };
        if (spec.SortKeyType is { } sortKeyType)
        {
            attributes.Add(new AttributeDefinition("sk", ScalarType(sortKeyType)));
            keySchema.Add(new KeySchemaElement("sk", KeyType.Range));
        }

        var indexes = new List<GlobalSecondaryIndex>();
        IEnumerable<GlobalIndexSpec> specs = spec.GlobalIndexes ?? [];
        foreach (var index in spec.WithGlobalIndex ? specs.Prepend(new GlobalIndexSpec(GlobalIndexName, "gsi_pk", SortKey: "gsi_sk")) : specs)
        {
            indexes.Add(AddGlobalIndex(attributes, index));
        }

        return new CreateTableRequest
        {
            TableName = name,
            AttributeDefinitions = attributes,
            KeySchema = keySchema,
            GlobalSecondaryIndexes = indexes.Count > 0 ? indexes : null,
            BillingMode = BillingMode.PayPerRequest,
            StreamSpecification = spec.StreamViewType is { } view ? new StreamSpecification { StreamEnabled = true, StreamViewType = view } : null,
        };
    }

    /// <summary>Updates a table, repeating the call when DynamoDB Local answers it with InternalFailure because other tables change at the same time.</summary>
    /// <param name="client">The client of the emulator.</param>
    /// <param name="request">The update.</param>
    /// <returns>The response of UpdateTable.</returns>
    public static async Task<UpdateTableResponse> UpdateAsync(DynamoDbClient client, UpdateTableRequest request)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.UpdateTableAsync(request);
            }
            catch (DynamoDbException exception) when (exception.ErrorCode == "InternalFailure" && attempt < 25)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(System.Security.Cryptography.RandomNumberGenerator.GetInt32(20, 150)));
            }
        }
    }

    // DynamoDB Local answers some CreateTable calls with InternalFailure (HTTP 500) while other tables are being created or deleted at
    // the same time, which parallel tests do all the time. The table does not exist after such an answer, so the call is repeated.
    // The client does not retry on its own (MaxAttempts is 1 in the tests) and would not repeat it as a throttle either.
    private static async Task<CreateTableResponse> CreateWithRetryAsync(DynamoDbClient client, CreateTableRequest request)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.CreateTableAsync(request);
            }
            catch (DynamoDbException exception) when (exception.ErrorCode == "InternalFailure" && attempt < 25)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(System.Security.Cryptography.RandomNumberGenerator.GetInt32(20, 150)));
            }
        }
    }

    private static GlobalSecondaryIndex AddGlobalIndex(List<AttributeDefinition> attributes, GlobalIndexSpec index)
    {
        Define(attributes, index.PartitionKey, index.PartitionKeyType);
        var keySchema = new List<KeySchemaElement> { new(index.PartitionKey, KeyType.Hash) };
        if (index.SortKey is { } sortKey)
        {
            Define(attributes, sortKey, index.SortKeyType);
            keySchema.Add(new KeySchemaElement(sortKey, KeyType.Range));
        }

        return new GlobalSecondaryIndex { IndexName = index.Name, KeySchema = keySchema, Projection = new IndexProjection { ProjectionType = ProjectionType.All } };
    }

    // An attribute can be a key of several indexes, and DynamoDB takes each definition once.
    private static void Define(List<AttributeDefinition> attributes, string name, KeyAttributeType type)
    {
        if (!attributes.Exists(attribute => attribute.AttributeName == name))
        {
            attributes.Add(new AttributeDefinition(name, ScalarType(type)));
        }
    }

    private static ScalarAttributeType ScalarType(KeyAttributeType type) => type switch
    {
        KeyAttributeType.String => ScalarAttributeType.String,
        KeyAttributeType.Number => ScalarAttributeType.Number,
        KeyAttributeType.Binary => ScalarAttributeType.Binary,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown key attribute type."),
    };

    // Dynoxide answers CreateTable with CREATING and activates the table right after; DynamoDB Local answers ACTIVE at once.
    private static async Task WaitUntilActiveAsync(DynamoDbClient client, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var response = await client.DescribeTableAsync(new DescribeTableRequest { TableName = name }, timeout.Token);
            if (response.Table.TableStatus == TableStatus.Active)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }
    }
}
