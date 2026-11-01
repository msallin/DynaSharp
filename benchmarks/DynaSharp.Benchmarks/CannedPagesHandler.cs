using System.Net.Http.Headers;
using System.Text;

namespace DynaSharp.Benchmarks;

/// <summary>A <see cref="CannedHandler"/> for reads that follow pages: it answers with its bodies in turn, starting over after the last one, which ends a read.</summary>
internal sealed class CannedPagesHandler(params string[] bodies) : HttpMessageHandler
{
    private readonly byte[][] _bodies = [.. bodies.Select(Encoding.UTF8.GetBytes)];
    private int _next;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(_bodies[_next]);
        _next = (_next + 1) % _bodies.Length;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.0");
        return Task.FromResult(new HttpResponseMessage { Content = content });
    }
}
