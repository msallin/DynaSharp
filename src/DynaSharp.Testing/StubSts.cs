namespace DynaSharp.Testing;

/// <summary>
/// A stand-in for AWS STS on a port of this machine, for a test of code that gets its credentials from a web identity token, as a pod with IAM roles for service accounts does. It answers
/// <c>AssumeRoleWithWebIdentity</c> with canned responses and records every request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a server and not a handler.</b> A provider of credentials has its own HTTP client, and <see cref="DynamoDbClientOptions.HttpHandler"/> carries the requests to DynamoDB and no others. So a <see cref="StubHandler"/> does not
/// see the fetch, and a test without this server would call the real STS. The stub is an HTTP/1.1 server on <c>127.0.0.1</c> and a free port, which it listens on from the constructor on. It makes no call to a network.
/// </para>
/// <para>
/// <b>Two ways to reach it.</b> Hand the provider the endpoint, which reads no variable, so the test can run in parallel with any other:
/// <c>new WebIdentityCredentialsProvider(tokenFile, roleArn, sts.Endpoint)</c>. Or set <c>AWS_ENDPOINT_URL_STS</c> to <see cref="Endpoint"/> yourself, with <c>AWS_WEB_IDENTITY_TOKEN_FILE</c> and <c>AWS_ROLE_ARN</c>, to go through the
/// default chain as a pod does. The stub never touches the process environment, because that would make every test that uses it unsafe to run beside the others; a test that changes the variables runs on its own, by the rule of its test framework.
/// </para>
/// <para>
/// <b>The token file is yours.</b> The provider reads a file that the test writes, and the stub does not touch the disk. It takes any token, and validates nothing.
/// </para>
/// <para>
/// The stub picks the response for a request in this order: the next response queued with <see cref="Enqueue"/>; <see cref="DefaultResponse"/>. If there is neither, it answers 400 with the code <c>StubStsNoResponse</c>, so a
/// test that forgot to queue reads <c>STS rejected the web identity token with StubStsNoResponse.</c> in the <see cref="CredentialsUnavailableException"/> of the provider. A provider retries some answers by itself: each attempt is a
/// request here, as for a <see cref="StubHandler"/>, and it waits a second between attempts on its own clock. That includes an attempt it gave up on: a provider on the system clock gives an attempt 5 seconds, so on a busy machine a queued
/// response can go to an attempt nobody waits for any more. A test that does not count fetches sets <see cref="DefaultResponse"/>, which answers them all.
/// </para>
/// <para>
/// Requests are answered one at a time, in the order they arrive. The stub is thread-safe, and with one port per instance tests with an instance each run in parallel.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var sts = new StubSts();
/// sts.Enqueue(StubStsResponse.Credentials(new AwsCredentials("ASIAEXAMPLE", "secret", "token", DateTimeOffset.UtcNow.AddHours(1))));
/// await File.WriteAllTextAsync(tokenFile, "a-token");
///
/// var provider = new WebIdentityCredentialsProvider(tokenFile, "arn:aws:iam::123456789012:role/orders", sts.Endpoint);
/// using var client = new DynamoDbClient(new DynamoDbClientOptions { Region = region, Credentials = provider, HttpHandler = stub });
/// // ... call the code under test, then:
/// var token = sts.Requests[0].WebIdentityToken;
/// </code>
/// </example>
public sealed class StubSts : IDisposable
{
    private static readonly StubStsResponse NoResponse =
        StubStsResponse.Error("StubStsNoResponse", "StubSts has no response for this request. Queue one with Enqueue before the request, or set DefaultResponse.");

    private readonly Lock _gate = new();
    private readonly Queue<StubStsResponse> _queued = new();
    private readonly List<RecordedStsRequest> _requests = [];
    private readonly LoopbackHttpServer _server;

    /// <summary>Initializes a new instance of the <see cref="StubSts"/> class, which listens from here on.</summary>
    public StubSts()
    {
        _server = new LoopbackHttpServer(Answer);
    }

    /// <summary>Gets the address of the stub, for example <c>http://127.0.0.1:50123/</c>: what a provider takes, or what <c>AWS_ENDPOINT_URL_STS</c> holds as <see cref="Uri.AbsoluteUri"/>.</summary>
    public Uri Endpoint => _server.Endpoint;

    /// <summary>
    /// Gets or sets the response for requests that no queued response answers, for a test that does not care how many fetches there are. <see langword="null"/> (the default) answers the error that names <c>StubStsNoResponse</c>.
    /// </summary>
    public StubStsResponse? DefaultResponse { get; set; }

    /// <summary>Gets the requests received so far, oldest first. The list is a snapshot; get it again after more requests. It stays readable after <see cref="Dispose"/>.</summary>
    public IReadOnlyList<RecordedStsRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Queues a response for a future request. Each queued response answers exactly one request, in the order queued.</summary>
    /// <param name="response">The response.</param>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    public void Enqueue(StubStsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            _queued.Enqueue(response);
        }
    }

    /// <summary>Stops listening and closes an open connection: a client in the middle of a request sees the connection end. It returns once the server has stopped.</summary>
    public void Dispose() => _server.Dispose();

    private ServerAnswer Answer(ReceivedRequest received)
    {
        var request = RecordedStsRequest.Create(received);
        StubStsResponse? response;
        lock (_gate)
        {
            _requests.Add(request);
            _queued.TryDequeue(out response);
        }

        response ??= DefaultResponse ?? NoResponse;
        return new ServerAnswer(response.StatusCode, response.BodyFor(request));
    }
}
