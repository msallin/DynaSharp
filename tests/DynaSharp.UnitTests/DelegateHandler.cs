namespace DynaSharp.UnitTests;

/// <summary>A handler that returns what a function creates, for responses a <c>StubHandler</c> cannot express.</summary>
internal sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}
