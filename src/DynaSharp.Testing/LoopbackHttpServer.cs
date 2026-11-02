using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DynaSharp.Testing;

/// <summary>
/// A server on a port of this machine that speaks plain HTTP/1.1 over a socket, for what a real <see cref="HttpClient"/> sends and no handler can see. It takes one connection at a time, answers one request on
/// it and closes it, so the requests are answered in the order they arrive and a client that holds a connection without finishing its request holds up the others until it leaves.
/// </summary>
internal sealed class LoopbackHttpServer : IDisposable
{
    private const string ContentLengthHeader = "Content-Length:";

    private static readonly byte[] EndOfHeaders = "\r\n\r\n"u8.ToArray();

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<ReceivedRequest, ServerAnswer> _answer;
    private readonly Task _serving;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="LoopbackHttpServer"/> class, which listens from here on.</summary>
    /// <param name="answer">Says what to answer to a request. It runs on the task of the server, one request at a time.</param>
    public LoopbackHttpServer(Func<ReceivedRequest, ServerAnswer> answer)
    {
        _answer = answer;
        _listener.Start();
        Endpoint = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/"));
        _serving = ServeAsync();
    }

    /// <summary>Gets the address the server listens on.</summary>
    public Uri Endpoint { get; }

    /// <summary>Stops listening and closes the connection that is open, if any.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // The listener goes last: an accept on a stopped listener throws InvalidOperationException, where one on a live listener sees the cancellation.
        _stop.Cancel();
        _serving.GetAwaiter().GetResult();
        _listener.Dispose();
        _stop.Dispose();
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var received = new ArrayBufferWriter<byte>();
        var buffer = new byte[4096];
        while (!IsComplete(received.WrittenSpan))
        {
            var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return [];
            }

            received.Write(buffer.AsSpan(0, count));
        }

        return received.WrittenSpan.ToArray();
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
        var line = headers.Split("\r\n").FirstOrDefault(static line => line.StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase));
        return line is null ? 0 : int.Parse(line[ContentLengthHeader.Length..], CultureInfo.InvariantCulture);
    }

    private static ReceivedRequest Parse(byte[] request)
    {
        var end = request.AsSpan().IndexOf(EndOfHeaders);
        var lines = Encoding.ASCII.GetString(request, 0, end).Split("\r\n");
        return new ReceivedRequest(lines[1..], Encoding.UTF8.GetString(request.AsSpan(end + EndOfHeaders.Length)));
    }

    // Without a reason phrase, which HTTP/1.1 allows and no client reads.
    private static byte[] Render(ServerAnswer answer)
    {
        var body = Encoding.UTF8.GetBytes(answer.Body);
        var head = string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {(int)answer.StatusCode} \r\nContent-Type: text/xml\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        return [.. Encoding.ASCII.GetBytes(head), .. body];
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            // A client that resets its connection or leaves halfway, and Dispose in the middle of all that, end this exchange and no other, so no failure here is the failure of the server.
            await ExchangeAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task ExchangeAsync()
    {
        using var connection = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
        var stream = connection.GetStream();
        var request = await ReadAsync(stream, _stop.Token).ConfigureAwait(false);

        // Nothing came, or the client left halfway: there is no request to record or to answer.
        if (request.Length > 0)
        {
            await stream.WriteAsync(Render(_answer(Parse(request))), _stop.Token).ConfigureAwait(false);
        }
    }
}
