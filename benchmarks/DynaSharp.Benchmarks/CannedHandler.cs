using System.Net.Http.Headers;
using System.Text;

namespace DynaSharp.Benchmarks;

/// <summary>
/// The one HTTP handler that all three clients of <see cref="ClientComparisonBenchmarks"/> send to: it answers every request with the
/// same canned DynamoDB response and does nothing else, so what is left in a measurement is the client. It reads the request
/// only while <see cref="Capture"/> is on, which the setup uses to check what each client sent. A handler that suspends yields
/// before it answers, as a network does, so the state machines of the client move to the heap, which a response that completes
/// synchronously never shows.
/// </summary>
internal sealed class CannedHandler : HttpMessageHandler
{
    private readonly byte[] _body;
    private readonly bool _suspend;

    public CannedHandler(string body, bool suspend = false)
    {
        _body = Encoding.UTF8.GetBytes(body);
        _suspend = suspend;
    }

    /// <summary>Gets or sets a value indicating whether requests are read and kept in <see cref="LastTarget"/> and <see cref="LastBody"/>.</summary>
    public bool Capture { get; set; }

    /// <summary>Gets the <c>X-Amz-Target</c> header of the last captured request.</summary>
    public string? LastTarget { get; private set; }

    /// <summary>Gets the body of the last captured request.</summary>
    public string? LastBody { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Capture || _suspend ? RespondAsync(request, cancellationToken) : Task.FromResult(CreateResponse());

    // Clients differ in whether the header sits on the request or on its content.
    private static string? FindTarget(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues("X-Amz-Target", out var values))
        {
            return values.Single();
        }

        return request.Content is { } content && content.Headers.TryGetValues("X-Amz-Target", out var contentValues) ? contentValues.Single() : null;
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_suspend)
        {
            await Task.Yield();
        }

        if (Capture)
        {
            LastTarget = FindTarget(request);
            LastBody = request.Content is { } content ? await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : null;
        }

        return CreateResponse();
    }

    private HttpResponseMessage CreateResponse()
    {
        var content = new ByteArrayContent(_body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.0");
        return new HttpResponseMessage { Content = content };
    }
}
