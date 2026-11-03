using System.Globalization;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>
/// A table for reads, on a sandbox of its own: string keys <c>pk</c> and <c>sk</c>, a global secondary index <c>ByStatus</c> over <c>status</c> and <c>placedAt</c>, and
/// a local secondary index <c>ByRank</c> over <c>rank</c>, both with all attributes.
/// </summary>
internal sealed class ReadSandbox : IAsyncDisposable
{
    public const string GlobalIndex = "ByStatus";

    public const string LocalIndex = "ByRank";

    private readonly Sandbox _sandbox;

    private ReadSandbox(Sandbox sandbox, string table)
    {
        _sandbox = sandbox;
        TableName = table;
        Shop = sandbox.Client.Table(table, "pk", "sk");
    }

    public DynamoDbClient Client => _sandbox.Client;

    public Emulator Emulator => _sandbox.Emulator;

    public string TableName { get; }

    public Table<Item> Shop { get; }

    public static async Task<ReadSandbox> StartAsync(Backend backend)
    {
        var sandbox = await Sandbox.StartAsync(backend);
        var table = Tables.UniqueName();
        await sandbox.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            AttributeDefinitions =
            [
                new("pk", ScalarAttributeType.String),
                new("sk", ScalarAttributeType.String),
                new("status", ScalarAttributeType.String),
                new("placedAt", ScalarAttributeType.String),
                new("rank", ScalarAttributeType.Number),
            ],
            KeySchema = [new("pk", KeyType.Hash), new("sk", KeyType.Range)],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = GlobalIndex,
                    KeySchema = [new("status", KeyType.Hash), new("placedAt", KeyType.Range)],
                    Projection = new IndexProjection { ProjectionType = ProjectionType.All },
                },
            ],
            LocalSecondaryIndexes =
            [
                new LocalSecondaryIndex
                {
                    IndexName = LocalIndex,
                    KeySchema = [new("pk", KeyType.Hash), new("rank", KeyType.Range)],
                    Projection = new IndexProjection { ProjectionType = ProjectionType.All },
                },
            ],
            BillingMode = BillingMode.PayPerRequest,
        });
        return new ReadSandbox(sandbox, table);
    }

    /// <summary>Gets the sort key of an order.</summary>
    /// <param name="number">The number of the order.</param>
    /// <returns>The sort key.</returns>
    public static string OrderKey(int number) => "ORDER#" + number.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>Puts <paramref name="count"/> orders into a partition: sort keys <c>ORDER#001</c> upwards, with a rank that falls as the sort key rises, an even or odd <c>kind</c>, and a total.</summary>
    /// <param name="pk">The partition key.</param>
    /// <param name="count">How many orders.</param>
    /// <param name="status">The status of all of them.</param>
    /// <returns>A task that completes when all items are written.</returns>
    public async Task PutOrdersAsync(string pk, int count, string status = "OPEN")
    {
        for (var i = 1; i <= count; i++)
        {
            await Shop.Put(new Item
            {
                ["pk"] = pk,
                ["sk"] = OrderKey(i),
                ["status"] = status,
                ["placedAt"] = "2026-" + i.ToString("D3", CultureInfo.InvariantCulture),
                ["rank"] = count - i,
                ["kind"] = i % 2 == 0 ? "even" : "odd",
                ["total"] = i,
            }).SendAsync();
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _sandbox.DisposeAsync();
}
