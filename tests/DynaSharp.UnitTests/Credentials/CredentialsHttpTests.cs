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
        var handler = (SocketsHttpHandler)CredentialsHttp.HandlerOf(client);
        await Assert.That((handler.AllowAutoRedirect, handler.UseCookies, handler.UseProxy)).IsEqualTo((false, false, false));
    }

    [Test]
    public async Task GetProxiedClient_NoHandler_UsesTheDefaultProxyAndNeitherRedirectsNorKeepsCookies()
    {
        // Arrange (STS is often reached through an egress proxy, and a redirect would send the web identity token to another host)

        // Act
        var client = CredentialsHttp.GetProxiedClient(null);

        // Assert
        var handler = (SocketsHttpHandler)CredentialsHttp.HandlerOf(client);
        await Assert.That((handler.UseProxy, handler.Proxy, handler.AllowAutoRedirect, handler.UseCookies)).IsEqualTo((true, (IWebProxy?)null, false, false));
    }

    [Test]
    [Arguments("http://127.0.0.1:4566/")]
    [Arguments("http://127.1.2.3:4566/")]
    [Arguments("http://localhost:4566/")]
    [Arguments("https://LOCALHOST/")]
    [Arguments("http://[::1]:4566/")]
    public async Task GetClientFor_Loopback_SendsPastTheProxy(string endpoint)
    {
        // Arrange (HttpClient.DefaultProxy sends loopback to HTTP_PROXY too, unless NO_PROXY says otherwise, and nothing behind a proxy can reach a port of this machine)

        // Act
        var client = CredentialsHttp.GetClientFor(new Uri(endpoint), null);

        // Assert
        await Assert.That(client).IsSameReferenceAs(CredentialsHttp.GetClient(null));
    }

    [Test]
    [Arguments("https://sts.eu-west-1.amazonaws.com/")]
    [Arguments("https://sts.example.com:8443/")]
    [Arguments("http://169.254.170.2/")]
    [Arguments("http://10.0.0.1:4566/")]
    public async Task GetClientFor_AnyOtherHost_UsesTheDefaultProxy(string endpoint)
    {
        // Arrange (STS is often reached through an egress proxy)

        // Act
        var client = CredentialsHttp.GetClientFor(new Uri(endpoint), null);

        // Assert
        await Assert.That(client).IsSameReferenceAs(CredentialsHttp.GetProxiedClient(null));
    }

    [Test]
    [Arguments("http://127.0.0.1:4566/")]
    [Arguments("https://sts.eu-west-1.amazonaws.com/")]
    public async Task GetClientFor_Handler_SendsThroughIt(string endpoint)
    {
        // Arrange
        using var handler = new DisposalTrackingHandler();

        // Act
        var client = CredentialsHttp.GetClientFor(new Uri(endpoint), handler);

        // Assert
        await Assert.That(CredentialsHttp.HandlerOf(client)).IsSameReferenceAs(handler);
    }

    [Test]
    public async Task GetProxiedClient_Handler_SendsThroughIt()
    {
        // Arrange
        using var handler = new DisposalTrackingHandler();

        // Act
        var client = CredentialsHttp.GetProxiedClient(handler);

        // Assert
        await Assert.That(CredentialsHttp.HandlerOf(client)).IsSameReferenceAs(handler);
    }

    [Test]
    public async Task GetClient_NoHandler_RecyclesConnectionsAfterTwoMinutes()
    {
        // Arrange

        // Act
        var client = CredentialsHttp.GetClient(null);

        // Assert
        var handler = (SocketsHttpHandler)CredentialsHttp.HandlerOf(client);
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
    public async Task SendAsync_ResponseWithADate_ReturnsIt()
    {
        // Arrange
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Headers = { Date = new DateTimeOffset(2026, 10, 31, 12, 44, 56, TimeSpan.Zero) } }));
        var client = CredentialsHttp.GetClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/creds");

        // Act
        var result = await CredentialsHttp.SendAsync(client, request, TimeSpan.FromMinutes(5), TimeProvider.System);

        // Assert
        await Assert.That(result.Date).IsEqualTo(new DateTimeOffset(2026, 10, 31, 12, 44, 56, TimeSpan.Zero));
    }

    [Test]
    public async Task SendAsync_ResponseWithoutADate_ReturnsNone()
    {
        // Arrange
        using var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = CredentialsHttp.GetClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/creds");

        // Act
        var result = await CredentialsHttp.SendAsync(client, request, TimeSpan.FromMinutes(5), TimeProvider.System);

        // Assert
        await Assert.That(result.Date).IsNull();
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
