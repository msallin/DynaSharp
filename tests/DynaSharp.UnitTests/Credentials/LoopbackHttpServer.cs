using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// A server on a port of this machine that speaks plain HTTP/1.1 over a socket and answers each request with the next body it was given, for a test of what a real <see cref="HttpClient"/> does. It sits on a
/// socket so that nothing of the library or of ASP.NET stands between the request and what a test sees. Past its bodies it answers 403, which a fetch does not retry on a clock that stands still, and it closes every connection, so a request is one connection.
/// </summary>
internal sealed class LoopbackHttpServer : IDisposable
{
    private static readonly byte[] EndOfHeaders = "\r\n\r\n"u8.ToArray();

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _bodies;
    private readonly Task _serving;

    public LoopbackHttpServer(params string[] bodies)
    {
        _bodies = new ConcurrentQueue<string>(bodies);
        _listener.Start();
        _serving = ServeAsync();
    }

    public Uri Endpoint => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        _serving.Wait();
        _stop.Dispose();
    }

    private static async Task<byte[]> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var received = new MemoryStream();
        var buffer = new byte[4096];
        while (!IsComplete(received.GetBuffer().AsSpan(0, (int)received.Length)))
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            await received.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        return received.ToArray();
    }

    private static bool IsComplete(ReadOnlySpan<byte> request)
    {
        var headers = request.IndexOf(EndOfHeaders);
        return headers >= 0 && request.Length >= headers + EndOfHeaders.Length + ContentLength(Encoding.ASCII.GetString(request[..headers]));
    }

    // Input:  "POST / HTTP/1.1\r\nContent-Length: 12\r\nHost: 127.0.0.1"
    // Result: 12
    private static int ContentLength(string headers)
    {
        var line = headers.Split("\r\n").FirstOrDefault(static line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        return line is null ? 0 : int.Parse(line["Content-Length:".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ReceivedRequest Parse(byte[] request)
    {
        var end = request.AsSpan().IndexOf(EndOfHeaders);
        var head = Encoding.ASCII.GetString(request, 0, end).Split("\r\n");
        return new ReceivedRequest(head[0], head[1..], Encoding.UTF8.GetString(request, end + EndOfHeaders.Length, request.Length - end - EndOfHeaders.Length));
    }

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                var stream = connection.GetStream();
                Requests.Enqueue(Parse(await ReadRequestAsync(stream, _stop.Token)));
                await stream.WriteAsync(Answer(), _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose stops the server.
        }
    }

    private byte[] Answer()
    {
        var found = _bodies.TryDequeue(out var body);
        var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        var status = found ? "200 OK" : "403 Forbidden";
        return [.. Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/xml\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), .. bytes];
    }

    /// <summary>A request as it came over the socket.</summary>
    /// <param name="RequestLine">The first line, such as <c>POST / HTTP/1.1</c>.</param>
    /// <param name="Headers">The lines of the headers.</param>
    /// <param name="Body">The body.</param>
    public sealed record ReceivedRequest(string RequestLine, IReadOnlyList<string> Headers, string Body);
}
