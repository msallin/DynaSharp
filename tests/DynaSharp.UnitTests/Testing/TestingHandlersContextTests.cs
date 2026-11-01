using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Text;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing;

// The handlers of DynaSharp.Testing are called from the code of the client under test, and must not resume on the context of that
// code either, see DynamoDbPipelineContextTests. Each test makes one step wait, and lets the ones before it complete at once.
[SuppressMessage("Reliability", "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed", Justification = "Every test awaits its call before it returns.")]
public sealed class TestingHandlersContextTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"Item":{}}""");

    [Test]
    public async Task StubHandler_RequestBodyArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var bodyReady = new TaskCompletionSource();
        using var stub = new StubHandler(_ => StubResponse.Json("{}"));
        using var invoker = new HttpMessageInvoker(stub);
        using var request = CreateRequest(bodyReady.Task);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => invoker.SendAsync(request, CancellationToken.None));

        // Act
        bodyReady.SetResult();
        using var response = await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task RecordingHandler_RequestBodyArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var bodyReady = new TaskCompletionSource();
        using var stub = new StubHandler(_ => StubResponse.Json("{}"));
        using var recorder = new RecordingHandler(stub);
        using var invoker = new HttpMessageInvoker(recorder);
        using var request = CreateRequest(bodyReady.Task);
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => invoker.SendAsync(request, CancellationToken.None));

        // Act
        bodyReady.SetResult();
        using var response = await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task RecordingHandler_InnerHandlerAnswersLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange
        var answerReady = new TaskCompletionSource();
        using var inner = new DelegateHandler(async (_, _) =>
        {
            await answerReady.Task.ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Body) };
        });
        using var recorder = new RecordingHandler(inner);
        using var invoker = new HttpMessageInvoker(recorder);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/");
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => invoker.SendAsync(request, CancellationToken.None));

        // Act
        answerReady.SetResult();
        using var response = await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task RecordingHandler_ResponseBodyArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the headers are there at once, the body is not)
        var bodyReady = new TaskCompletionSource();
        using var inner = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new GatedStream(Body, bodyReady.Task)),
        }));
        using var recorder = new RecordingHandler(inner);
        using var invoker = new HttpMessageInvoker(recorder);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/");
        var context = new CountingSynchronizationContext();
        var call = context.Start(() => invoker.SendAsync(request, CancellationToken.None));

        // Act
        bodyReady.SetResult();
        using var response = await call;

        // Assert
        await Assert.That(context.Posts).IsEqualTo(0);
    }

    [Test]
    public async Task RecordingHandler_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange (HttpMessageInvoker rejects null before the handler sees it, so the handler is called the way a derived handler would)
        using var stub = new StubHandler();
        using var recorder = new RecordingHandler(stub);
        var send = typeof(RecordingHandler).GetMethod("SendAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Act
        var act = async () => await (Task<HttpResponseMessage>)send.Invoke(recorder, [null, CancellationToken.None])!;

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("request");
    }

    [Test]
    public async Task RecordingHandler_Response_ReplacesTheContentWithOneThatKnowsItsOwnLength()
    {
        // Arrange (the content claims a length that the body does not have)
        using var inner = new DelegateHandler((_, _) =>
        {
            var content = new ByteArrayContent(Body);
            content.Headers.ContentLength = Body.Length + 10;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var recorder = new RecordingHandler(inner);
        using var invoker = new HttpMessageInvoker(recorder);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/");

        // Act
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        await Assert.That(response.Content.Headers.ContentLength).IsEqualTo(Body.Length);
    }

    [Test]
    public async Task RecordingHandler_Response_DisposesTheOriginalContent()
    {
        // Arrange
        using var original = new TrackingContent(Body);
        using var inner = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = original }));
        using var recorder = new RecordingHandler(inner);
        using var invoker = new HttpMessageInvoker(recorder);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/");

        // Act
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        await Assert.That(original.Disposed).IsTrue();
    }

    private static HttpRequestMessage CreateRequest(Task bodyReady) => new(HttpMethod.Post, "http://localhost/")
    {
        Content = new StreamContent(new GatedStream(Body, bodyReady)),
    };

    private sealed class TrackingContent(byte[] body) : ByteArrayContent(body)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
