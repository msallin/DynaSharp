using System.Globalization;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// Creates and deletes the tables of the tests through the client's CreateTable and DeleteTable. This is the one place that knows
/// how a test table is made. Raw HTTP stays available through the emulator for tests that need the unmapped response.
/// </summary>
/// <remarks>
/// Every name starts with <see cref="Prefix"/>, which is what the IAM role of real AWS is limited to, then the time and the id of the run, as in
/// <c>dynasharp-it-20261101T120000-1a2b3c4d-...</c>. Each emulator run starts empty, so there this is only a name. On real AWS it tells the tables of this run, and those of runs that died.
/// </remarks>
internal static class Tables
{
    /// <summary>The name of the global secondary index of a table made with <see cref="TableSpec.WithGlobalIndex"/>.</summary>
    public const string GlobalIndexName = "gsi";

    /// <summary>What the name of every table starts with.</summary>
    public const string Prefix = "dynasharp-it-";

    private const string StartFormat = "yyyyMMdd'T'HHmmss";

    private static readonly string RunPrefix =
        $"{Prefix}{DateTime.UtcNow.ToString(StartFormat, CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..8]}-";

    /// <summary>Gets the name of a table that does not exist. It has the prefix, so that a limited role gets to hear that from DynamoDB and not from IAM.</summary>
    public static string Missing { get; } = RunPrefix + "does-not-exist";

    /// <summary>Creates a name that no other test uses.</summary>
    /// <returns>The table name.</returns>
    public static string UniqueName() => $"{RunPrefix}{Guid.NewGuid():N}";

    /// <summary>Tells whether a table was made by this run.</summary>
    /// <param name="name">The name of the table.</param>
    /// <returns><see langword="true"/> if it was.</returns>
    public static bool IsOfThisRun(string name) => name.StartsWith(RunPrefix, StringComparison.Ordinal);

    /// <summary>Tells whether a table comes from a run that started before a given time. Any other table of the account is not one of these.</summary>
    /// <param name="name">The name of the table.</param>
    /// <param name="time">The time.</param>
    /// <returns><see langword="true"/> if the table has the prefix and its run started earlier.</returns>
    public static bool StartedBefore(string name, DateTimeOffset time) =>
        name.StartsWith(Prefix, StringComparison.Ordinal)
        && name.Length >= Prefix.Length + StartFormat.Length - 2
        && DateTimeOffset.TryParseExact(name.AsSpan(Prefix.Length, StartFormat.Length - 2), StartFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start)
        && start < time;

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
        var response = await RepeatOnInternalFailureAsync(() => client.CreateTableAsync(request));
        await WaitUntilActiveAsync(client, request.TableName);
        return response;
    }

    /// <summary>Sends a command that creates a table, repeated like every other creation here.</summary>
    /// <param name="command">The command.</param>
    /// <returns>What the command returns.</returns>
    public static Task<CreateTableResult> SendAsync(CreateTableCommand command) => RepeatOnInternalFailureAsync(async () => await command.SendAsync());

    /// <summary>Describes the table until <paramref name="condition"/> holds for it, for the changes that UpdateTable makes asynchronously.</summary>
    /// <param name="client">The client of the emulator.</param>
    /// <param name="name">The name of the table.</param>
    /// <param name="condition">What the description must show.</param>
    /// <returns>The description for which the condition held.</returns>
    /// <exception cref="OperationCanceledException">The condition did not hold within ten minutes. An emulator takes milliseconds; real DynamoDB builds an index that long.</exception>
    public static async Task<TableDescription> WaitUntilAsync(DynamoDbClient client, string name, Func<TableDescription, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        while (true)
        {
            var description = (await client.DescribeTableAsync(name, timeout.Token)).Table!;
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

    /// <summary>Deletes a table, waiting for as long as it is busy: real DynamoDB refuses while a table is created or updated.</summary>
    /// <param name="client">The client of the backend.</param>
    /// <param name="name">The name of the table.</param>
    /// <returns>A task that completes when the backend accepted the deletion.</returns>
    /// <exception cref="OperationCanceledException">The table was still busy after five minutes.</exception>
    public static async Task DeleteAsync(DynamoDbClient client, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        while (true)
        {
            try
            {
                await client.DeleteTableAsync(new DeleteTableRequest { TableName = name }, timeout.Token);
                return;
            }
            catch (ResourceInUseException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
            }
        }
    }

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
    public static Task<UpdateTableResponse> UpdateAsync(DynamoDbClient client, UpdateTableRequest request) =>
        RepeatOnInternalFailureAsync(() => client.UpdateTableAsync(request));

    // DynamoDB Local answers some CreateTable calls with InternalFailure (HTTP 500) while other tables are being created or deleted at
    // the same time, which parallel tests do all the time. The table does not exist after such an answer, so the call is repeated.
    // The client does not retry on its own (MaxAttempts is 1 in the tests) and would not repeat it as a throttle either.
    private static async Task<T> RepeatOnInternalFailureAsync<T>(Func<Task<T>> call)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (DynamoDbException exception) when (exception.ErrorCode == "InternalFailure" && attempt < 25)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(System.Security.Cryptography.RandomNumberGenerator.GetInt32(20, 150)));
            }
            catch (LimitExceededException) when (attempt < 25)
            {
                // Real DynamoDB limits the tables that change at the same time, which a run may touch. Space frees up as fast as tables turn active.
                await Task.Delay(TimeSpan.FromSeconds(attempt));
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

    // Dynoxide answers CreateTable with CREATING and activates the table right after; DynamoDB Local answers ACTIVE at once. Real DynamoDB takes
    // tens of seconds, more with indexes, and for its first moments may not even know the table, which the wait of a table bears with.
    private static async Task WaitUntilActiveAsync(DynamoDbClient client, string name) => await client.Table(name, "pk").WaitUntilActiveAsync();
}
