using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using DynaSharp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies temporary credentials of the IAM role of an Amazon EC2 instance from the instance metadata service (IMDS), and
/// refreshes them before they expire.
/// </summary>
/// <remarks>
/// <para>
/// A fetch has three steps, as in botocore: <c>PUT /latest/api/token</c> with a token lifetime of 6 hours (IMDSv2), then
/// <c>GET /latest/meta-data/iam/security-credentials/</c> for the role name, then the same path plus the role name for the
/// credentials, both with the token. Every request has 1 second and is tried once. If the token request answers 403, 404 or 405,
/// or times out, the service does not offer IMDSv2 (or the hop limit swallows the answer) and the provider falls back to
/// IMDSv1, which is the same requests without a token. Any other failure of the token request, such as a refused connection
/// or a server error, ends the fetch. <c>AWS_EC2_METADATA_V1_DISABLED=true</c>, or <c>ec2_metadata_v1_disabled = true</c> in the selected profile, turns the fallback off.
/// </para>
/// <para>
/// <c>AWS_EC2_METADATA_DISABLED=true</c> (not case sensitive) switches the provider off: <see cref="GetCredentialsAsync"/> then
/// throws a <see cref="CredentialsUnavailableException"/> that says so, without any network traffic.
/// <c>AWS_EC2_METADATA_SERVICE_ENDPOINT</c>, or <c>ec2_metadata_service_endpoint</c> in the selected profile, replaces the endpoint
/// <c>http://169.254.169.254</c>; it must be an absolute http or https URL without user info. <c>AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE</c>, or
/// <c>ec2_metadata_service_endpoint_mode</c>, set to <c>IPv6</c> (not case sensitive) selects <c>http://[fd00:ec2::254]</c> instead, unless an endpoint is set.
/// The variable goes ahead of the profile. A malformed config file is ignored. Redirects, proxies and cookies are not used.
/// </para>
/// <para>
/// Not being on an EC2 instance is the normal reason for a failure, so every problem of the service (unreachable, no role
/// attached, an error document, a response without the four fields <c>AccessKeyId</c>, <c>SecretAccessKey</c>, <c>Token</c> and
/// <c>Expiration</c>) is reported as a <see cref="CredentialsUnavailableException"/> with the reason; a wrong endpoint
/// variable as well. Nothing is retried within a call, and a failure is not cached.
/// </para>
/// <para>
/// The same <see cref="AwsCredentials"/> instance is returned until it is refreshed. Refreshing starts 15 minutes before the
/// expiration. In that advisory period the fetch runs in the background and every caller keeps the current credentials, so a slow
/// source delays no request; if the fetch fails a warning is logged and it is tried again after 30 seconds. From 10 minutes before the expiration every caller waits for the
/// fetch, and a failure is thrown. If DynamoDB rejects the credentials as expired before that, a client drops them with
/// <see cref="TryInvalidate"/> and the next call fetches. Concurrent callers share one fetch, which a caller's cancellation does not cancel.
/// The provider is thread-safe.
/// </para>
/// <para>
/// As botocore does, the provider limits how often it asks again when the service keeps handing out credentials that are
/// about to expire (the service renews them at least five minutes before they expire, and not at all while it has a problem):
/// if the received credentials expire within 15 minutes plus a random 2 to 10 minutes, the <see cref="AwsCredentials.Expiration"/>
/// of the returned instance is set to that point in the future instead. The next refresh is then attempted 2 to 10 minutes
/// later, rather than with every request. The credentials themselves are unchanged and may expire earlier than they say.
/// </para>
/// </remarks>
public sealed class InstanceMetadataCredentialsProvider : IRefreshableCredentialsProvider
{
    private const string DisabledVariable = "AWS_EC2_METADATA_DISABLED";
    private const string V1DisabledVariable = "AWS_EC2_METADATA_V1_DISABLED";
    private const string EndpointVariable = "AWS_EC2_METADATA_SERVICE_ENDPOINT";
    private const string EndpointSetting = "ec2_metadata_service_endpoint";
    private const string EndpointModeVariable = "AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE";
    private const string EndpointModeSetting = "ec2_metadata_service_endpoint_mode";
    private const string V1DisabledSetting = "ec2_metadata_v1_disabled";
    private const string DefaultEndpoint = "http://169.254.169.254";
    private const string Ipv6Endpoint = "http://[fd00:ec2::254]";
    private const string TokenPath = "latest/api/token";
    private const string RolePath = "latest/meta-data/iam/security-credentials/";
    private const string SourceName = "EC2 instance metadata service";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly ReadTextFile _readFile;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly Random _random;
    private readonly RefreshingCredentialsCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceMetadataCredentialsProvider"/> class that reads the environment of the
    /// current process and uses the system clock.
    /// </summary>
    public InstanceMetadataCredentialsProvider()
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, null, TimeProvider.System, NullLogger.Instance, null)
    {
    }

    internal InstanceMetadataCredentialsProvider(
        Func<string, string?> getEnvironmentVariable,
        ReadTextFile readFile,
        HttpMessageHandler? handler,
        TimeProvider timeProvider,
        ILogger logger,
        Random? random)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _readFile = readFile;
        _http = CredentialsHttp.GetClient(handler);
        _timeProvider = timeProvider;
        _random = random ?? Random.Shared;
        _cache = new RefreshingCredentialsCache(SourceName, timeProvider, logger, FetchAsync);
    }

    /// <summary>
    /// Gets the credentials of the instance role, fetching or refreshing them when needed.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops this call from waiting. The fetch itself, which other callers may share, is not cancelled.
    /// </param>
    /// <returns>The same <see cref="AwsCredentials"/> instance until it is refreshed.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// The service is disabled by <c>AWS_EC2_METADATA_DISABLED</c>, cannot be reached, has no role or no valid credentials, or the
    /// endpoint variable is invalid. During the mandatory refresh period, the failure of a refresh is thrown as well.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var outcome = await TryGetCredentialsAsync(cancellationToken).ConfigureAwait(false);
        return outcome.GetCredentials(SourceName);
    }

    /// <summary>
    /// Drops credentials that DynamoDB rejected as expired, so that the next <see cref="GetCredentialsAsync"/> fetches new ones.
    /// A client does this by itself.
    /// </summary>
    /// <param name="rejected">The instance that was rejected. Any other than the current one has been replaced already, and is ignored.</param>
    /// <returns>Always <see langword="true"/>: this source can be asked again.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rejected"/> is <see langword="null"/>.</exception>
    public bool TryInvalidate(AwsCredentials rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        _cache.Drop(rejected);
        return true;
    }

    internal ValueTask<CredentialsOutcome> TryGetCredentialsAsync(CancellationToken cancellationToken) =>
        IsTrue(_getEnvironmentVariable(DisabledVariable))
            ? ValueTask.FromResult(CredentialsOutcome.DoesNotApply($"it is disabled by {DisabledVariable}=true"))
            : _cache.GetAsync(cancellationToken);

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    // Input: "my-role\n" (a second line would be ignored)   Result: "my-role"
    private static string FirstLine(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        var end = text.IndexOf('\n', StringComparison.Ordinal);
        return (end < 0 ? text : text[..end]).Trim();
    }

    private string ResolveBaseUrl()
    {
        var text = SharedConfigFiles.ReadSetting(_getEnvironmentVariable, _readFile, EndpointVariable, EndpointSetting) ?? DefaultEndpointOfMode();

        // The value is not echoed: it may carry secrets in its user info.
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
            ? text.TrimEnd('/')
            : throw new CredentialsUnavailableException($"{EndpointVariable} or {EndpointSetting} must be an absolute http or https URL without user info.");
    }

    private string DefaultEndpointOfMode()
    {
        var mode = SharedConfigFiles.ReadSetting(_getEnvironmentVariable, _readFile, EndpointModeVariable, EndpointModeSetting);
        if (mode is null || string.Equals(mode, "IPv4", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultEndpoint;
        }

        return string.Equals(mode, "IPv6", StringComparison.OrdinalIgnoreCase)
            ? Ipv6Endpoint
            : throw new CredentialsUnavailableException($"{EndpointModeVariable} or {EndpointModeSetting} must be IPv4 or IPv6.");
    }

    // A problem of the service counts as "does not apply", because off EC2 that is what it means, and this is the last source of the
    // default chain: there is nothing it could fall through to. A source that comes after it must change this.
    private async ValueTask<CredentialsOutcome> FetchAsync()
    {
        var baseUrl = ResolveBaseUrl();
        var token = await RequestTokenAsync(baseUrl).ConfigureAwait(false);
        if (token.Problem is { } tokenProblem)
        {
            return CredentialsOutcome.DoesNotApply(tokenProblem);
        }

        if (token.Value is null && IsTrue(SharedConfigFiles.ReadSetting(_getEnvironmentVariable, _readFile, V1DisabledVariable, V1DisabledSetting)))
        {
            return CredentialsOutcome.DoesNotApply($"the IMDSv2 token is not available and IMDSv1 is disabled by {V1DisabledVariable}=true or {V1DisabledSetting}=true");
        }

        var role = await GetAsync($"{baseUrl}/{RolePath}", token.Value).ConfigureAwait(false);
        if (!role.IsOk)
        {
            return CredentialsOutcome.DoesNotApply(
                role.StatusCode == HttpStatusCode.NotFound
                    ? "no IAM role is attached to the instance"
                    : $"the request for the role name {role.DescribeFailure()}");
        }

        var roleName = FirstLine(role.Body);
        if (roleName.Length == 0)
        {
            return CredentialsOutcome.DoesNotApply("the service returned no role name");
        }

        var response = await GetAsync($"{baseUrl}/{RolePath}{Uri.EscapeDataString(roleName)}", token.Value).ConfigureAwait(false);
        if (!response.IsOk)
        {
            return CredentialsOutcome.DoesNotApply($"the request for the credentials {response.DescribeFailure()}");
        }

        if (!CredentialsDocument.TryParse(response.Body, out var document))
        {
            return CredentialsOutcome.DoesNotApply("the response is not a JSON object");
        }

        // An instance whose role cannot be assumed gets HTTP 200 with {"Code":"AssumeRoleUnauthorizedAccess","Message":"..."}.
        return document.TryCreateCredentials(out var credentials) is { } problem
            ? CredentialsOutcome.DoesNotApply(document.Code is { } code ? $"{problem} (the service returned the code {code})" : problem)
            : CredentialsOutcome.Found(LimitRefreshRate(credentials!));
    }

    // The IMDSv2 token. A null Value without a Problem means: use IMDSv1.
    private async ValueTask<(string? Value, string? Problem)> RequestTokenAsync(string baseUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{baseUrl}/{TokenPath}");
        request.Headers.TryAddWithoutValidation("x-aws-ec2-metadata-token-ttl-seconds", "21600");
        var result = await CredentialsHttp.SendAsync(_http, request, RequestTimeout, _timeProvider).ConfigureAwait(false);
        return result.Kind switch
        {
            CredentialsHttp.ResultKind.TimedOut => (null, null),
            CredentialsHttp.ResultKind.Failed => (null, $"the token request {result.DescribeFailure()}"),
            _ => result.StatusCode switch
            {
                HttpStatusCode.OK => (FirstLine(result.Body) is { Length: > 0 } token ? token : null, null),
                HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => (null, null),
                _ => (null, $"the token request {result.DescribeFailure()}"),
            },
        };
    }

    private async Task<CredentialsHttp.Result> GetAsync(string url, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("x-aws-ec2-metadata-token", token);
        }

        return await CredentialsHttp.SendAsync(_http, request, RequestTimeout, _timeProvider).ConfigureAwait(false);
    }

    // See the remarks of the class. The window and the jitter are botocore's.
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Jitter that spreads the refreshes of many hosts; nothing depends on its unpredictability.")]
    private AwsCredentials LimitRefreshRate(AwsCredentials credentials)
    {
        var now = _timeProvider.GetUtcNow();
        var window = RefreshingCredentialsCache.AdvisoryWindow + TimeSpan.FromSeconds(_random.Next(120, 601));
        return credentials.Expiration!.Value - window <= now
            ? new AwsCredentials(credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken, now + window)
            : credentials;
    }
}
