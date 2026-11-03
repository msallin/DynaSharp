using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>A table with string keys <c>pk</c> and <c>sk</c>, for tests of the command API, on a sandbox of its own.</summary>
internal sealed class CommandSandbox : IAsyncDisposable
{
    private readonly Sandbox _sandbox;

    private CommandSandbox(Sandbox sandbox, string table)
    {
        _sandbox = sandbox;
        Client = sandbox.Client;
        TableName = table;
        Shop = sandbox.Client.Table(table, "pk", "sk");
    }

    public DynamoDbClient Client { get; }

    public string TableName { get; }

    public Table<Item> Shop { get; }

    public Emulator Emulator => _sandbox.Emulator;

    public static async Task<CommandSandbox> StartAsync(Backend backend)
    {
        var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.String));
        return new CommandSandbox(sandbox, table);
    }

    /// <summary>Creates another table with string keys <c>pk</c> and <c>sk</c>.</summary>
    /// <returns>The table.</returns>
    public async Task<Table<Item>> CreateTableAsync() =>
        Client.Table(await _sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.String)), "pk", "sk");

    /// <summary>Reads an item with a strongly consistent read.</summary>
    /// <param name="pk">The partition key.</param>
    /// <param name="sk">The sort key.</param>
    /// <returns>The item, or <see langword="null"/> if there is none.</returns>
    public async Task<Item?> ReadAsync(string pk, string sk = "s") => (await Shop.Get(pk, sk).Consistent().SendAsync()).Item;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _sandbox.DisposeAsync();
}
