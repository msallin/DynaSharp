using DynaSharp.IntegrationTests.Backends;
using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// What one test works with: clients for its backend and the tables it created. Nothing is shared between tests
/// but the backend, so tests may run in parallel and in any order. Dispose it at the end of the test to drop its tables.
/// </summary>
internal sealed class Sandbox : IAsyncDisposable
{
    private readonly List<string> _tables = [];
    private readonly SettlingHandler? _handler;

    private Sandbox(Emulator emulator, DynamoDbClient client, DynamoDbStreamsClient streams, SettlingHandler? handler)
    {
        Emulator = emulator;
        Client = client;
        Streams = streams;
        _handler = handler;
    }

    /// <summary>Gets the backend the test runs against.</summary>
    public Emulator Emulator { get; }

    /// <summary>Gets a client for the backend. For an emulator it does not retry, so a failure shows up at once.</summary>
    public DynamoDbClient Client { get; }

    /// <summary>Gets a DynamoDB Streams client for the same backend. Both emulators serve streams on the port of the table API.</summary>
    public DynamoDbStreamsClient Streams { get; }

    /// <summary>Starts the backend if needed and creates the clients for it.</summary>
    /// <param name="backend">The backend of the test.</param>
    /// <returns>The sandbox.</returns>
    public static async Task<Sandbox> StartAsync(Backend backend)
    {
        var emulator = await Emulators.GetAsync(backend);
        var handler = backend == Backend.RealAws ? new SettlingHandler() : null;
        var options = emulator.CreateOptions() with { HttpHandler = handler };
        return new Sandbox(emulator, new DynamoDbClient(options), new DynamoDbStreamsClient(options), handler);
    }

    /// <summary>Creates a table with a unique name that is dropped when the sandbox is disposed.</summary>
    /// <param name="spec">The shape of the table; a string partition key <c>pk</c> by default.</param>
    /// <returns>The name of the table.</returns>
    public async Task<string> CreateTableAsync(TableSpec? spec = null)
    {
        var name = Tables.UniqueName();
        await CreateTableAsync(Tables.Request(name, spec ?? TableSpec.PartitionOnly));
        return name;
    }

    /// <summary>Creates the table of <paramref name="request"/>, which is dropped when the sandbox is disposed.</summary>
    /// <param name="request">The table; use <see cref="Tables.UniqueName"/> for its name.</param>
    /// <returns>The response of CreateTable.</returns>
    public async Task<CreateTableResponse> CreateTableAsync(CreateTableRequest request)
    {
        // Before the call: a create whose wait fails has made the table all the same.
        Track(request.TableName);
        return await Tables.CreateAsync(Client, request);
    }

    /// <summary>Sends a command that creates a table, which is dropped when the sandbox is disposed.</summary>
    /// <typeparam name="T">What the table reads and writes.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="command">The command, one of <paramref name="table"/>.</param>
    /// <returns>What the command returns.</returns>
    public async Task<CreateTableResult> CreateTableAsync<T>(Table<T> table, CreateTableCommand command)
        where T : IDynamoDbDocument<T>
    {
        Track(table.Name);
        return await Tables.SendAsync(command);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var table in _tables)
        {
            try
            {
                await Tables.DeleteAsync(Client, table);
            }
            catch (ResourceNotFoundException)
            {
                // The test deleted the table itself.
            }
        }

        Client.Dispose();
        Streams.Dispose();
        _handler?.Dispose();
    }

    // A test may send a create for a table it has, which then is deleted once.
    private void Track(string table)
    {
        if (!_tables.Contains(table))
        {
            _tables.Add(table);
        }
    }
}
