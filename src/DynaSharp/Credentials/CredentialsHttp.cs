using System.Net;

namespace DynaSharp.Credentials;

/// <summary>
/// The HTTP clients of the credential endpoints. They are separate from the DynamoDB traffic: <see cref="DynamoDbClientOptions.HttpHandler"/>
/// never sees these requests, and these requests never follow redirects or keep cookies. A redirect would send the token to another host.
/// The container endpoint and instance metadata are link-local, so their client never uses the proxy. AWS STS is a public endpoint that is often
/// reached through an egress proxy, so it has a client of its own that uses the default proxy of the process, and <see cref="GetClientFor"/> tells the two apart for an endpoint that is configured.
/// </summary>
internal static class CredentialsHttp
{
    /// <summary>The attempts of <see cref="SendWithRetriesAsync"/>, as in the standard retry mode of botocore.</summary>
    public const int MaxAttempts = 3;

    // Credentials documents are about 1.5 KB; anything near this size is not one.
    private const int MaxResponseBytes = 64 * 1024;

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    // One client for all providers, as HttpClient is meant to be used. The endpoints are link-local or loopback addresses,
    // so connections are cheap to recycle and a stale one is never a problem for long.
    private static readonly HttpClient SharedClient = CreateClient(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        });

    private static readonly HttpClient ProxiedClient = CreateClient(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        });

    /// <summary>How a request ended.</summary>
    public enum ResultKind
    {
        /// <summary>The server answered. See <see cref="Result.StatusCode"/> and <see cref="Result.Body"/>.</summary>
        Response,

        /// <summary>The server did not answer in time.</summary>
        TimedOut,

        /// <summary>The request could not be sent or the response not be read, for example because the connection was refused.</summary>
        Failed,
    }

    /// <summary>Returns the shared client, or a client over <paramref name="handler"/> (for tests) if there is one.</summary>
    /// <param name="handler">The handler to send through, or <see langword="null"/> for the shared client.</param>
    /// <returns>The client. Do not dispose it.</returns>
    public static HttpClient GetClient(HttpMessageHandler? handler) => handler is null ? SharedClient : CreateClient(handler);

    /// <summary>Returns the client that uses the default proxy, or a client over <paramref name="handler"/> (for tests) if there is one.</summary>
    /// <param name="handler">The handler to send through, or <see langword="null"/> for the shared client.</param>
    /// <returns>The client. Do not dispose it.</returns>
    public static HttpClient GetProxiedClient(HttpMessageHandler? handler) => handler is null ? ProxiedClient : CreateClient(handler);

    /// <summary>
    /// Returns the client for a public endpoint that may also be configured to something else: the one that uses the default proxy, or for a loopback address the one that does not. A proxy cannot reach a port of this
    /// machine, and the default proxy says that it can: <c>HTTP_PROXY</c> covers loopback too, unless <c>NO_PROXY</c> names it.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="handler">The handler to send through, or <see langword="null"/> for a shared client.</param>
    /// <returns>The client. Do not dispose it.</returns>
    public static HttpClient GetClientFor(Uri endpoint, HttpMessageHandler? handler) => endpoint.IsLoopback ? GetClient(handler) : GetProxiedClient(handler);

    /// <summary>
    /// Sends a request until the answer is not transient, at most <see cref="MaxAttempts"/> times, one second apart. Each attempt gets 5 seconds, so a
    /// failing exchange stays within the default <see cref="DynamoDbClientOptions.AttemptTimeout"/> of a client.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="createRequest">Creates the request of an attempt: a request can be sent once only, and a signed one carries the time of its signature.</param>
    /// <param name="isTransient">Tells whether a result is worth another attempt.</param>
    /// <param name="timeProvider">The clock of the timeouts and the pauses.</param>
    /// <returns>The first result that is not transient, or the last one.</returns>
    public static async Task<Result> SendWithRetriesAsync(HttpClient client, Func<HttpRequestMessage> createRequest, Func<Result, bool> isTransient, TimeProvider timeProvider)
    {
        for (var attempt = 1; ; attempt++)
        {
            Result result;
            using (var request = createRequest())
            {
                result = await SendAsync(client, request, AttemptTimeout, timeProvider).ConfigureAwait(false);
            }

            if (attempt == MaxAttempts || !isTransient(result))
            {
                return result;
            }

            await Task.Delay(RetryDelay, timeProvider).ConfigureAwait(false);
        }
    }

    /// <summary>Tells whether a result is transient for any credentials endpoint: no answer in time, a failure of the network, HTTP 429 or 5xx.</summary>
    /// <param name="result">The result.</param>
    /// <returns><see langword="true"/> if another attempt may succeed.</returns>
    public static bool IsTransientFailure(Result result) =>
        result.Kind != ResultKind.Response || (int)result.StatusCode >= 500 || result.StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>Sends a request and reads the whole response, in at most <paramref name="timeout"/>.</summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request.</param>
    /// <param name="timeout">How long the whole exchange may take.</param>
    /// <param name="timeProvider">The clock of the timeout.</param>
    /// <returns>The outcome. Never throws for timeouts and network errors.</returns>
    public static async Task<Result> SendAsync(HttpClient client, HttpRequestMessage request, TimeSpan timeout, TimeProvider timeProvider)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        try
        {
            using var response = await client.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(timeoutSource.Token).ConfigureAwait(false);
            return new Result(ResultKind.Response, response.StatusCode, body, null);
        }
        catch (OperationCanceledException)
        {
            return new Result(ResultKind.TimedOut, default, [], null);
        }
        catch (HttpRequestException exception)
        {
            return new Result(ResultKind.Failed, default, [], exception.HttpRequestError.ToString());
        }
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler, disposeHandler: false)
    {
        // Timeouts are applied per request with the configured TimeProvider.
        Timeout = Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = MaxResponseBytes,
    };

    /// <summary>The outcome of <see cref="SendAsync"/>.</summary>
    /// <param name="Kind">How the request ended.</param>
    /// <param name="StatusCode">The HTTP status code, when <paramref name="Kind"/> is <see cref="ResultKind.Response"/>.</param>
    /// <param name="Body">The response body, when <paramref name="Kind"/> is <see cref="ResultKind.Response"/>.</param>
    /// <param name="Error">The name of the <see cref="HttpRequestError"/>, when <paramref name="Kind"/> is <see cref="ResultKind.Failed"/>.</param>
    public readonly record struct Result(ResultKind Kind, HttpStatusCode StatusCode, byte[] Body, string? Error)
    {
        /// <summary>Gets a value indicating whether the server answered with HTTP 200.</summary>
        public bool IsOk => Kind == ResultKind.Response && StatusCode == HttpStatusCode.OK;

        /// <summary>Describes a request that did not end in a response, for messages: <c>timed out</c> or <c>failed (ConnectionError)</c>.</summary>
        /// <returns>The description.</returns>
        public string DescribeFailure() => Kind switch
        {
            ResultKind.TimedOut => "timed out",
            ResultKind.Failed => $"failed ({Error})",
            _ => $"HTTP {(int)StatusCode}",
        };
    }
}
