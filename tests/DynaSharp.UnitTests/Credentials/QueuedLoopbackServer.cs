using System.Collections.Concurrent;
using System.Net;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// A <see cref="LoopbackHttpServer"/> that answers each request with the next body it was given and records what came, for a test of what a real <see cref="HttpClient"/> does.
/// Past its bodies it answers 403, which a fetch does not retry on a clock that stands still.
/// </summary>
internal sealed class QueuedLoopbackServer : IDisposable
{
    private readonly ConcurrentQueue<string> _bodies;
    private readonly LoopbackHttpServer _server;

    public QueuedLoopbackServer(params string[] bodies)
    {
        _bodies = new ConcurrentQueue<string>(bodies);
        _server = new LoopbackHttpServer(Answer);
    }

    public Uri Endpoint => _server.Endpoint;

    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    public void Dispose() => _server.Dispose();

    private ServerAnswer Answer(ReceivedRequest request)
    {
        Requests.Enqueue(request);
        return _bodies.TryDequeue(out var body) ? new ServerAnswer(HttpStatusCode.OK, body) : new ServerAnswer(HttpStatusCode.Forbidden, string.Empty);
    }
}
