using System.Diagnostics.CodeAnalysis;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>
/// A single table with the keys of the shop model, on a sandbox of its own: two global indexes and a local one, and two global ones that have the
/// discriminator as a key, everything projected.
/// </summary>
internal sealed class ShopSandbox : IAsyncDisposable
{
    public const string ByType = "ByType";

    public const string ByPartitionAndType = "ByPartitionAndType";

    private readonly Sandbox _sandbox;

    private ShopSandbox(Sandbox sandbox, string table)
    {
        _sandbox = sandbox;
        Shop = sandbox.Client.Table(table, "pk", "sk");
        Customers = sandbox.Client.Table<Customer>(table);
        Orders = sandbox.Client.Table<Order>(table);
        Comments = Shop.Of<Comment>();
        Visits = Shop.Of<Visit>();
        Tickets = Shop.Of<Ticket>();
        Markers = Shop.Of<EmailMarker>();
        Claims = Shop.Of<Claim>();
        Client = sandbox.Client;
    }

    public Table<Item> Shop { get; }

    public Table<Customer> Customers { get; }

    public Table<Order> Orders { get; }

    public Table<Comment> Comments { get; }

    public Table<Visit> Visits { get; }

    public Table<Ticket> Tickets { get; }

    public Table<EmailMarker> Markers { get; }

    public Table<Claim> Claims { get; }

    public DynamoDbClient Client { get; }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The object that is returned owns the sandbox.")]
    public static async Task<ShopSandbox> StartAsync(Backend backend)
    {
        var sandbox = await Sandbox.StartAsync(backend);
        var name = Tables.UniqueName();
        await sandbox.CreateTableAsync(new CreateTableRequest
        {
            TableName = name,
            BillingMode = BillingMode.PayPerRequest,
            AttributeDefinitions =
            [
                Text("pk"), Text("sk"), Text("gsi1pk"), Text("gsi1sk"), Text("gsi2pk"), Text("gsi2sk"), Text("lsi1sk"), Text("_type"),
            ],
            KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)],
            GlobalSecondaryIndexes =
            [
                GlobalIndex("GSI1", "gsi1pk", "gsi1sk"), GlobalIndex("GSI2", "gsi2pk", "gsi2sk"),
                GlobalIndex(ByType, "_type", "sk"), GlobalIndex(ByPartitionAndType, "pk", "_type"),
            ],
            LocalSecondaryIndexes =
            [
                new LocalSecondaryIndex
                {
                    IndexName = "LSI1",
                    KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("lsi1sk", KeyType.Range)],
                    Projection = new IndexProjection { ProjectionType = ProjectionType.All },
                },
            ],
        });
        return new ShopSandbox(sandbox, name);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _sandbox.DisposeAsync();

    private static AttributeDefinition Text(string name) => new(name, ScalarAttributeType.String);

    private static GlobalSecondaryIndex GlobalIndex(string name, string partitionKey, string sortKey) => new()
    {
        IndexName = name,
        KeySchema = [new KeySchemaElement(partitionKey, KeyType.Hash), new KeySchemaElement(sortKey, KeyType.Range)],
        Projection = new IndexProjection { ProjectionType = ProjectionType.All },
    };
}
