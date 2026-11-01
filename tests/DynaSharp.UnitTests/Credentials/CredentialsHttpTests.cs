using System.Diagnostics.CodeAnalysis;
using System.Net;
using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Credentials;

[SuppressMessage("Reliability", "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed", Justification = "Every test awaits its call before it returns.")]
public sealed class CredentialsHttpTests
{
    [Test]
    public async Task GetClient_NoHandler_SendsThroughAHandlerThatNeitherRedirectsNorKeepsCookiesNorUsesAProxy()
    {
        // Arrange (a redirect would send the authorization token to another host)

        // Act
        var client = CredentialsHttp.GetClient(null);

        // Assert
        var handler = (SocketsHttpHandler)HttpClientInspector.HandlerOf(client);
        await Assert.That((handler.AllowAutoRedirect, handler.UseCookies, handler.UseProxy)).IsEqualTo((false, false, false));
    }

    [Test]
    public async Task GetClient_NoHandler_RecyclesConnectionsAfterTwoMinutes()
    {
        // Arrange

        // Act
        var client = CredentialsHttp.GetClient(null);

        // Assert
        var handler = (SocketsHttpHandler)HttpClientInspector.HandlerOf(client);
        await Assert.That(handler.PooledConnectionLifetime).IsEqualTo(TimeSpan.FromMinutes(2));
    }

    [Test]
    public async Task GetClient_Handler_SharesItWithTheCallerWhoOwnsIt()
    {
        // Arrange
        using var handler = new DisposalTrackingHandler();
        var client = CredentialsHttp.GetClient(handler);

        // Act
        client.Dispose();

        // Assert
        await Assert.That(handler.Disposed).IsFalse();
    }

    [Test]
    public async Task SendAsync_NoAnswerInTime_ReturnsTimedOutWithoutABody()
    {
        // Arrange (the handler answers only when the request is cancelled, which the clock does when the timeout is over)
        var time = new ManualTimeProvider();
        using var handler = new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        });
        var client = CredentialsHttp.GetClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/creds");
        var send = CredentialsHttp.SendAsync(client, request, TimeSpan.FromSeconds(1), time);

        // Act
        time.Advance(TimeSpan.FromSeconds(1));
        var result = await send;

        // Assert
        await Assert.That((result.Kind, result.Body.Length)).IsEqualTo((CredentialsHttp.ResultKind.TimedOut, 0));
    }

    [Test]
    public async Task SendAsync_BodyArrivesLater_DoesNotResumeOnTheContextOfTheCaller()
    {
        // Arrange (the headers are there at once, the body is not)
        var bodyReady = new TaskCompletionSource();
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new GatedStream([1, 2, 3], bodyReady.Task)),
        }));
        var client = CredentialsHttp.GetClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/creds");
        var context = new CountingSynchronizationContext();
        var send = context.Start(() => CredentialsHttp.SendAsync(client, request, TimeSpan.FromMinutes(5), TimeProvider.System));

        // Act
        bodyReady.SetResult();
        var result = await send;

        // Assert
        await Assert.That((context.Posts, result.Body.Length)).IsEqualTo((0, 3));
    }

    [Test]
    public async Task SendAsync_ConnectionFails_ReturnsFailedWithTheNameOfTheErrorAndWithoutABody()
    {
        // Arrange
        using var handler = new DelegateHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        var client = CredentialsHttp.GetClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/creds");

        // Act
        var result = await CredentialsHttp.SendAsync(client, request, TimeSpan.FromMinutes(5), TimeProvider.System);

        // Assert
        await Assert.That((result.Kind, result.Error, result.Body.Length)).IsEqualTo((CredentialsHttp.ResultKind.Failed, "ConnectionError", 0));
    }

    private sealed class DisposalTrackingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
