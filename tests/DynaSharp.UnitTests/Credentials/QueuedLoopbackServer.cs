using System.Collections.Concurrent;
using System.Net;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// A <see cref="LoopbackHttpServer"/> that answers each request with the next answer it was given and records what came, for a test of what a real <see cref="HttpClient"/> does.
/// Past its bodies it answers 403, which a fetch does not retry on a clock that stands still.
/// </summary>
internal sealed class QueuedLoopbackServer : IDisposable
{
    private readonly ConcurrentQueue<ServerAnswer> _answers;
    private readonly LoopbackHttpServer _server;

    public QueuedLoopbackServer(params string[] bodies)
        : this(bodies.Select(static body => new ServerAnswer(HttpStatusCode.OK, body)))
    {
    }

    private QueuedLoopbackServer(IEnumerable<ServerAnswer> answers)
    {
        _answers = new ConcurrentQueue<ServerAnswer>(answers);
        _server = new LoopbackHttpServer(Answer);
    }

    public Uri Endpoint => _server.Endpoint;

    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    /// <summary>A server that answers with these statuses, bodies and extra header lines, in order.</summary>
    public static QueuedLoopbackServer WithAnswers(params ServerAnswer[] answers) => new(answers);

    public void Dispose() => _server.Dispose();

    private ServerAnswer Answer(ReceivedRequest request)
    {
        Requests.Enqueue(request);
        return _answers.TryDequeue(out var answer) ? answer : new ServerAnswer(HttpStatusCode.Forbidden, string.Empty);
    }
}
