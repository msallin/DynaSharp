using DynaSharp.Credentials;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// What one test works with: clients for the emulator of its backend and the tables it created. Nothing is shared between tests
/// but the emulator, so tests may run in parallel and in any order. Dispose it at the end of the test to drop its tables.
/// </summary>
internal sealed class Sandbox : IAsyncDisposable
{
    private readonly List<string> _tables = [];

    private Sandbox(Emulator emulator, DynamoDbClient client, DynamoDbStreamsClient streams)
    {
        Emulator = emulator;
        Client = client;
        Streams = streams;
    }

    /// <summary>Gets the emulator the test runs against.</summary>
    public Emulator Emulator { get; }

    /// <summary>Gets a client for the emulator. It does not retry, so a failure shows up at once.</summary>
    public DynamoDbClient Client { get; }

    /// <summary>Gets a DynamoDB Streams client for the same endpoint. Both emulators serve streams on the port of the table API.</summary>
    public DynamoDbStreamsClient Streams { get; }

    /// <summary>Starts the emulator of <paramref name="backend"/> if needed and creates the clients for it.</summary>
    /// <param name="backend">The backend of the test.</param>
    /// <returns>The sandbox.</returns>
    public static async Task<Sandbox> StartAsync(Backend backend)
    {
        var emulator = await Emulators.GetAsync(backend);
        var options = new DynamoDbClientOptions
        {
            Region = AwsRegion.FromName("us-east-1"),
            ServiceUrl = emulator.Endpoint,
            Credentials = new StaticCredentialsProvider(new AwsCredentials("test", "test")),
            MaxAttempts = 1,
            AttemptTimeout = TimeSpan.FromMinutes(2),
        };
        return new Sandbox(emulator, new DynamoDbClient(options), new DynamoDbStreamsClient(options));
    }

    /// <summary>Creates a table with a unique name that is dropped when the sandbox is disposed.</summary>
    /// <param name="spec">The shape of the table; a string partition key <c>pk</c> by default.</param>
    /// <returns>The name of the table.</returns>
    public async Task<string> CreateTableAsync(TableSpec? spec = null)
    {
        var name = await Tables.CreateAsync(Client, spec ?? TableSpec.PartitionOnly);
        _tables.Add(name);
        return name;
    }

    /// <summary>Creates the table of <paramref name="request"/>, which is dropped when the sandbox is disposed.</summary>
    /// <param name="request">The table; use <see cref="Tables.UniqueName"/> for its name.</param>
    /// <returns>The response of CreateTable.</returns>
    public async Task<CreateTableResponse> CreateTableAsync(CreateTableRequest request)
    {
        var response = await Tables.CreateAsync(Client, request);
        _tables.Add(request.TableName);
        return response;
    }

    /// <summary>Sends a command that creates a table, which is dropped when the sandbox is disposed.</summary>
    /// <typeparam name="T">What the table reads and writes.</typeparam>
    /// <param name="table">The table.</param>
    /// <param name="command">The command, one of <paramref name="table"/>.</param>
    /// <returns>The description the command returns.</returns>
    public async Task<TableDescription> CreateTableAsync<T>(Table<T> table, CreateTableCommand command)
        where T : IDynamoDbDocument<T>
    {
        var description = await Tables.SendAsync(command);
        _tables.Add(table.Name);
        return description;
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
    }
}
