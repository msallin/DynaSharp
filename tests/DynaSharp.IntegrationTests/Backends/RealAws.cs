using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// DynamoDB itself as a backend of the integration tests. It takes the region and the endpoint from the environment and the credentials from
/// <see cref="DefaultCredentialsProvider"/>, as an application would, so a run through <c>.github/workflows/real-aws.yml</c> also exercises web identity end to end.
/// </summary>
/// <remarks>
/// <para>
/// Every table has the prefix of <see cref="Tables"/>, and is on demand unless a test is about provisioned capacity, so a run costs cents and the role needs nothing else.
/// A table goes with its test, in <see cref="Sandbox.DisposeAsync"/>. What a test leaves behind because it failed halfway goes when the run ends, and what a run leaves behind
/// because it was killed goes at the start of the first run after <see cref="OrphanAge"/>. A failure to delete fails the run, so that a leak does not stay unnoticed.
/// </para>
/// <para>
/// The difference between this and an emulator that matters most is timing: see <see cref="SettlingHandler"/> and the remarks of <see cref="Gaps"/>. The clients retry, which the
/// clients of an emulator do not: real DynamoDB may throttle a new table, and a test that sees that has found nothing.
/// </para>
/// </remarks>
internal static class RealAws
{
    // One provider for the whole run. A client would otherwise make its own, which is a call of STS per test.
    private static readonly DefaultCredentialsProvider Credentials = new();

    // Longer than a run may take, see timeout-minutes in the workflow, so that a run never touches the tables of another one.
    private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(6);

    /// <summary>Clears out the orphans of earlier runs and prepares the raw clients.</summary>
    /// <returns>The backend.</returns>
    public static async Task<Emulator> ConnectAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await DeleteTablesAsync(name => Tables.StartedBefore(name, now - OrphanAge));
        return new Emulator(Backend.RealAws, CreateOptions, CreateRaw(), CreateRawStreams());
    }

    /// <summary>Deletes what the tests of this run left behind.</summary>
    /// <returns>A task that completes when the tables are gone.</returns>
    public static Task DeleteTablesOfThisRunAsync() => DeleteTablesAsync(Tables.IsOfThisRun);

    private static DynamoDbClientOptions CreateOptions() => new() { Credentials = Credentials, AttemptTimeout = TimeSpan.FromMinutes(2) };

    private static async Task DeleteTablesAsync(Func<string, bool> selected)
    {
        using var client = new DynamoDbClient(CreateOptions());
        string? start = null;
        do
        {
            var page = await client.ListTablesAsync(new ListTablesRequest { ExclusiveStartTableName = start });
            foreach (var name in page.TableNames.Where(selected))
            {
                try
                {
                    await Tables.DeleteAsync(client, name);
                }
                catch (ResourceNotFoundException)
                {
                    // It was on its way out.
                }
            }

            start = page.LastEvaluatedTableName;
        }
        while (start is not null);
    }

    // The client signs and sends, the handler keeps what came back: the tests want the response as it was on the wire.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Lives as long as the run.")]
    private static RawDynamoDb CreateRaw()
    {
        var wire = new WireHandler();
        var client = new DynamoDbClient(CreateOptions() with { MaxAttempts = 1, HttpHandler = wire });
        return new RawDynamoDb((operation, json, cancellationToken) => wire.CaptureAsync(() => client.SendRawAsync(operation, Encoding.UTF8.GetBytes(json), cancellationToken)));
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Lives as long as the run.")]
    private static RawDynamoDb CreateRawStreams()
    {
        var wire = new WireHandler();
        var client = new DynamoDbStreamsClient(CreateOptions() with { MaxAttempts = 1, HttpHandler = wire });
        return new RawDynamoDb((operation, json, cancellationToken) => wire.CaptureAsync(() => client.SendRawAsync(operation, Encoding.UTF8.GetBytes(json), cancellationToken)));
    }

    /// <summary>Hands the response of the current call back to its caller, whatever the client makes of it.</summary>
    private sealed class WireHandler : DelegatingHandler
    {
        private readonly AsyncLocal<StrongBox<RawResponse>> _current = new();

#pragma warning disable CA2000 // The base class takes the handler and disposes it.
        public WireHandler()
            : base(new SocketsHttpHandler())
#pragma warning restore CA2000
        {
        }

        public async Task<RawResponse> CaptureAsync(Func<Task<byte[]>> call)
        {
            var captured = new StrongBox<RawResponse>();
            _current.Value = captured;
            try
            {
                await call();
            }
            catch (DynamoDbException)
            {
                // That is one of the responses a test may want: it is in the box.
            }

            return captured.Value!;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var requestId = response.Headers.TryGetValues("x-amzn-RequestId", out var values) ? values.FirstOrDefault() : null;
            _current.Value!.Value = new RawResponse(response.StatusCode, body, requestId);
            return response;
        }
    }
}
