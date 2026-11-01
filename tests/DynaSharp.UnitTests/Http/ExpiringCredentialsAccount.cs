using System.Globalization;
using System.Net;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Credentials;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Http;

/// <summary>
/// A DynamoDB and a container credentials endpoint that agree on the time, which may differ from the clock of the test. The endpoint
/// hands out <c>key-1</c>, <c>key-2</c> and so on, and the service judges each request like the real one: first the signing time, then
/// the access key against the expiry of that key.
/// </summary>
internal sealed class ExpiringCredentialsAccount : IDisposable
{
    public const string SecretPrefix = "secret-of-";
    public const string TokenPrefix = "token-of-";

    private readonly ManualTimeProvider _time;
    private readonly TimeSpan _serviceAhead;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _expirations = [];
    private readonly HashSet<string> _revoked = [];
    private int _fetches;

    /// <param name="time">The clock of the test, which is the local clock of the client.</param>
    /// <param name="serviceAhead">How far the service is ahead of that clock.</param>
    public ExpiringCredentialsAccount(ManualTimeProvider time, TimeSpan serviceAhead = default)
    {
        _time = time;
        _serviceAhead = serviceAhead;
        Service = new StubHandler(RespondAsync);
        Endpoint = new StubHandler(_ => IssueCredentials());
    }

    /// <summary>Gets the handler of DynamoDB.</summary>
    public StubHandler Service { get; }

    /// <summary>Gets the handler of the container credentials endpoint.</summary>
    public StubHandler Endpoint { get; }

    /// <summary>Gets how long the credentials of a fetch are valid.</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Gets the error code of a request signed with expired credentials.</summary>
    public string ExpiredCode { get; init; } = "ExpiredTokenException";

    /// <summary>Gets the body of a successful response.</summary>
    public string SuccessJson { get; init; } = TestClient.GetItemResponseJson;

    /// <summary>Gets or sets a value indicating whether the service rejects every key as expired, as with a stale identity.</summary>
    public bool RejectEverything { get; set; }

    /// <summary>Gets or sets a value indicating whether the credentials endpoint answers with a document that has no credentials.</summary>
    public bool EndpointBroken { get; set; }

    /// <summary>Gets or sets what a request waits for before the service answers it.</summary>
    public Func<Task>? BeforeAnswer { get; set; }

    /// <summary>Gets the number of successful fetches of the credentials endpoint.</summary>
    public int Fetches => Volatile.Read(ref _fetches);

    /// <summary>Gets the access key of each request that reached the service, oldest first.</summary>
    public IReadOnlyList<string> KeysUsed => [.. Service.Requests.Select(static request => AccessKeyOf(request))];

    private DateTimeOffset ServiceNow => _time.GetUtcNow() + _serviceAhead;

    /// <summary>Makes the service reject a key as expired from now on, whatever its expiry says.</summary>
    /// <param name="accessKeyId">The key.</param>
    public void Revoke(string accessKeyId)
    {
        lock (_gate)
        {
            _revoked.Add(accessKeyId);
        }
    }

    public void Dispose()
    {
        Service.Dispose();
        Endpoint.Dispose();
    }

    private static string AccessKeyOf(RecordedRequest request)
    {
        // Input:  "AWS4-HMAC-SHA256 Credential=key-1/20261031/eu-central-2/dynamodb/aws4_request, SignedHeaders=..."
        // Result: "key-1"
        const string marker = "Credential=";
        var authorization = request.Headers["Authorization"];
        var start = authorization.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return authorization[start..authorization.IndexOf('/', start, StringComparison.Ordinal)];
    }

    private StubResponse IssueCredentials()
    {
        if (EndpointBroken)
        {
            return StubResponse.Json("{}");
        }

        var key = $"key-{Interlocked.Increment(ref _fetches)}";
        var expiration = ServiceNow + Lifetime;
        lock (_gate)
        {
            _expirations[key] = expiration;
        }

        return StubResponse.Json(CredentialsTestSupport.ContainerJson(expiration, key, SecretPrefix + key, TokenPrefix + key));
    }

    private async Task<StubResponse> RespondAsync(RecordedRequest request, CancellationToken cancellationToken)
    {
        if (BeforeAnswer is { } beforeAnswer)
        {
            await beforeAnswer().WaitAsync(cancellationToken);
        }

        var now = ServiceNow;
        var signedAt = DateTimeOffset.ParseExact(
            request.Headers["X-Amz-Date"],
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        if ((signedAt - now).Duration() > TimeSpan.FromMinutes(5))
        {
            return SkewRejection(now);
        }

        var key = AccessKeyOf(request);
        bool expired;
        lock (_gate)
        {
            expired = RejectEverything || _revoked.Contains(key) || !_expirations.TryGetValue(key, out var expiration) || expiration <= now;
        }

        return expired
            ? StubResponse.DynamoDbError(ExpiredCode, "The security token included in the request is expired", HttpStatusCode.BadRequest)
            : StubResponse.Json(SuccessJson);
    }
}
