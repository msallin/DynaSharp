using System.Net;
using System.Text;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Http;

// A library must not resume on the context of its caller: a caller that blocks on the task of a call (.Result on a UI thread, or on an
// old ASP.NET request) would deadlock. Each test makes one step of a call wait, so that the step after it is the first await that
// suspends, and then looks at whether the continuation was posted to the context that was current when the call started.
public sealed class DynamoDbPipelineContextTests
{
    [Test]
    public async Task ExecuteAsync_CredentialsArriveLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var credentialsReady = new TaskCompletionSource<AwsCredentials>();
        using var handler = new StubHandler(_ => StubResponse.Json(TestClient.GetItemResponseJson));
        using var client = TestClient.Create(handler, credentials: new PendingCredentialsProvider(credentialsReady.Task));
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        credentialsReady.SetResult(TestClient.Credentials);
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_ResponseHeadersArriveLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var answerReady = new TaskCompletionSource();
        using var handler = new DelegateHandler(async (_, _) =>
        {
            await answerReady.Task.ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestClient.GetItemResponseJson) };
        });
        using var client = TestClient.Create(handler);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        answerReady.SetResult();
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_BodyArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the headers are there at once, the body is not)
        var bodyReady = new TaskCompletionSource();
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new GatedStream(Encoding.UTF8.GetBytes(TestClient.GetItemResponseJson), bodyReady.Task)),
        }));
        using var client = TestClient.Create(handler);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        bodyReady.SetResult();
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_WaitBeforeARetryEnds_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the first attempt is throttled and finishes at once, so the wait is the first await that suspends)
        var time = new ManualTimeProvider();
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(Success());
        using var client = CreateClient(handler, time, random: FixedRandom.One);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => client.GetItemAsync(TestClient.GetItem()));

        // Act
        await AdvanceThroughDelaysAsync(call, time, TestClient.DefaultAttemptTimeout);
        await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    private sealed class PendingCredentialsProvider(Task<AwsCredentials> credentials) : ICredentialsProvider
    {
        public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) => new(credentials);
    }
}
