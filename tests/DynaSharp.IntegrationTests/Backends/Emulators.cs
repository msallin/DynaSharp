using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DotNet.Testcontainers.Builders;
using DynaSharp.Credentials;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// Starts each selected emulator once per test run, on first use, and stops them when the run ends. Tests share an emulator and
/// keep apart by using a table of their own (see <c>Sandbox</c>). Real AWS has no container, and goes through here for the same two moments of a run.
/// </summary>
internal static class Emulators
{
    private const int ContainerPort = 8000;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

    private static readonly ConcurrentDictionary<Backend, Lazy<Task<Running>>> Started = new();

    /// <summary>Starts the emulator of <paramref name="backend"/> if it is not running yet.</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>The running emulator.</returns>
    public static async Task<Emulator> GetAsync(Backend backend)
    {
        var running = await Started.GetOrAdd(backend, static key => new Lazy<Task<Running>>(() => StartAsync(key))).Value;
        return running.Emulator;
    }

    /// <summary>Stops every emulator this run started, and deletes what is left of its tables on real AWS.</summary>
    /// <returns>A task that completes when the containers and the tables are gone.</returns>
    [After(HookType.Assembly)]
    public static async Task StopAllAsync()
    {
        foreach (var start in Started.Values)
        {
            if (start.Value.IsCompletedSuccessfully)
            {
                var running = await start.Value;
                await running.Stop();
            }
        }
    }

    /// <summary>Reads the image from the FROM line of the backend's Dockerfile, so Dependabot's updates reach the tests.</summary>
    /// <param name="backend">The backend.</param>
    /// <returns>The image reference, for example <c>amazon/dynamodb-local:3.3.2</c>.</returns>
    internal static string ReadImage(Backend backend)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Backends", FolderOf(backend), "Dockerfile");
        return ParseImage(File.ReadAllLines(path), path);
    }

    internal static string ParseImage(IEnumerable<string> dockerfileLines, string source)
    {
        foreach (var line in dockerfileLines)
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2 && string.Equals(words[0], "FROM", StringComparison.OrdinalIgnoreCase))
            {
                return words[1];
            }
        }

        throw new InvalidOperationException($"{source} has no FROM line.");
    }

    // When adding a backend, add its folder here (see Backend).
    private static string FolderOf(Backend backend) => backend switch
    {
        Backend.Dynoxide => "dynoxide",
        Backend.DynamoDbLocal => "dynamodb-local",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown backend."),
    };

    private static Task<Running> StartAsync(Backend backend) => backend == Backend.RealAws ? ConnectToRealAwsAsync() : StartContainerAsync(backend);

    private static async Task<Running> ConnectToRealAwsAsync() => new Running(await RealAws.ConnectAsync(), RealAws.DeleteTablesOfThisRunAsync);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Owned by the Running record, disposed with the container.")]
    private static async Task<Running> StartContainerAsync(Backend backend)
    {
        var builder = new ContainerBuilder(ReadImage(backend)).WithPortBinding(ContainerPort, assignRandomHostPort: true);
        if (backend == Backend.DynamoDbLocal)
        {
            // In memory, one database for all credentials and regions. The Dynoxide image already starts "serve" on port 8000.
            builder = builder.WithCommand("-jar", "DynamoDBLocal.jar", "-inMemory", "-sharedDb");
        }

        var container = builder.Build();
        await container.StartAsync();

        var endpoint = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(ContainerPort)}");
        var http = new HttpClient();
        var raw = new RawDynamoDb(endpoint, http);
        await WaitUntilReadyAsync(raw, backend);
        var emulator = new Emulator(backend, () => CreateOptions(endpoint), raw, new RawDynamoDb(endpoint, http, RawDynamoDb.StreamsTargetPrefix));
        return new Running(emulator, async () =>
        {
            http.Dispose();
            await container.DisposeAsync();
        });
    }

    // MaxAttempts of 1: an emulator has no reason to fail a request twice, so a failure shows up at once.
    private static DynamoDbClientOptions CreateOptions(Uri endpoint) => new()
    {
        Region = AwsRegion.FromName("us-east-1"),
        ServiceUrl = endpoint,
        Credentials = new StaticCredentialsProvider(new AwsCredentials("test", "test")),
        MaxAttempts = 1,
        AttemptTimeout = TimeSpan.FromMinutes(2),
    };

    // A container reports "started" before the emulator listens. The Dynoxide image has no shell, so no wait strategy that
    // runs a command inside it works; asking the API itself works for both.
    private static async Task WaitUntilReadyAsync(RawDynamoDb raw, Backend backend)
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        while (true)
        {
            try
            {
                (await raw.SendAsync("ListTables", "{}", timeout.Token)).EnsureSuccess();
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException && !timeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"{backend} did not answer ListTables within {StartupTimeout.TotalSeconds:0} seconds.");
            }
        }
    }

    private sealed record Running(Emulator Emulator, Func<Task> Stop);
}
