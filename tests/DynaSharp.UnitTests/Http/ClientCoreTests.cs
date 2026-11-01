using System.Diagnostics;
using System.Reflection;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Http;
using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Http;

public sealed class ClientCoreTests
{
    [Test]
    public async Task Create_WithoutHandler_LeavesTheTimeoutToTheAttempts()
    {
        // Arrange
        var options = Options();

        // Act
        using var core = ClientCore.Create<DynamoDbClient>(AwsService.DynamoDb, options, static _ => null, null, null, null, null);

        // Assert
        await Assert.That(HttpClientOf(core).Timeout).IsEqualTo(Timeout.InfiniteTimeSpan);
    }

    [Test]
    public async Task Create_WithHandler_LeavesTheTimeoutToTheAttempts()
    {
        // Arrange
        using var handler = new StubHandler();
        var options = Options(handler);

        // Act
        using var core = ClientCore.Create<DynamoDbClient>(AwsService.DynamoDb, options, static _ => null, null, null, null, null);

        // Assert
        await Assert.That(HttpClientOf(core).Timeout).IsEqualTo(Timeout.InfiniteTimeSpan);
    }

    [Test]
    public async Task Dispose_WithoutHandler_DisposesTheHandlerTheCoreCreated()
    {
        // Arrange
        var core = ClientCore.Create<DynamoDbClient>(AwsService.DynamoDb, Options(), static _ => null, null, null, null, null);
        var handler = (SocketsHttpHandler)HttpClientInspector.HandlerOf(HttpClientOf(core));

        // Act
        core.Dispose();

        // Assert (a disposed handler refuses to be configured)
        var act = () => handler.AllowAutoRedirect = true;
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task Create_ActivitySourceGiven_TheOperationsStartTheirActivitiesFromIt()
    {
        // Arrange (a listener for this source only, so that no other test is seen)
        using var source = new ActivitySource($"DynaSharp.Tests.{Guid.NewGuid():N}");
        var sources = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (sources)
                {
                    sources.Add(activity.Source.Name);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, activitySource: source);

        // Act
        await client.GetItemAsync(TestClient.GetItem());

        // Assert
        await Assert.That(sources.Single()).IsEqualTo(source.Name);
    }

    private static DynamoDbClientOptions Options(HttpMessageHandler? handler = null) => new()
    {
        Region = TestClient.Region,
        Credentials = new StaticCredentialsProvider(TestClient.Credentials),
        HttpHandler = handler,
    };

    // The HTTP client is private on purpose; what it is set to is the contract of the core, so the tests look at it.
    private static HttpClient HttpClientOf(ClientCore core) =>
        (HttpClient)typeof(ClientCore).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(core)!;
}
