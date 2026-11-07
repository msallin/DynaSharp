using System.Net;
using DynaSharp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies temporary credentials from the container credentials endpoint of Amazon ECS (task roles) and Amazon EKS (Pod
/// Identity), and refreshes them before they expire.
/// </summary>
/// <remarks>
/// <para>
/// The provider applies when <c>AWS_CONTAINER_CREDENTIALS_RELATIVE_URI</c> or <c>AWS_CONTAINER_CREDENTIALS_FULL_URI</c> is set
/// and not empty; otherwise <see cref="GetCredentialsAsync"/> throws a <see cref="CredentialsUnavailableException"/> that says so
/// (the default chain then moves on). The relative URI is appended to <c>http://169.254.170.2</c> and wins if both are set. The
/// full URI must be an absolute URL without user info; over http its host must be a loopback address or one of the container
/// endpoint addresses <c>169.254.170.2</c>, <c>169.254.170.23</c> and <c>fd00:ec2::23</c> (or <c>localhost</c>), https accepts any host.
/// This is the rule of botocore.
/// </para>
/// <para>
/// The authorization token, if any, is sent as the <c>Authorization</c> header. It comes from the file named by
/// <c>AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE</c>, which wins, or else from <c>AWS_CONTAINER_AUTHORIZATION_TOKEN</c>. The file is
/// read again for every fetch, because EKS rotates it; leading and trailing whitespace is removed. A token with line breaks is
/// rejected.
/// </para>
/// <para>
/// A fetch is a <c>GET</c> that must answer 200 with a JSON object that has <c>AccessKeyId</c>, <c>SecretAccessKey</c>,
/// <c>Token</c> and <c>Expiration</c>. Each attempt has 2 seconds; a timeout, a connection error, another HTTP status and a body
/// that is not JSON are retried up to 3 attempts in total, one second apart. If all fail, or the configuration is wrong (the
/// URI, the token file), or the response lacks a field or holds credentials that are already expired, the call throws a
/// <see cref="CredentialsUnavailableException"/>. Once the endpoint is configured, the default chain never goes on to another
/// source, so a pod never signs with the instance role of its node. Redirects, proxies and cookies are not used.
/// </para>
/// <para>
/// The same <see cref="AwsCredentials"/> instance is returned until it is refreshed. Refreshing starts 15 minutes before the
/// expiration. In that advisory period the fetch runs in the background and every caller keeps the current credentials, so a slow
/// source delays no request; if the fetch fails a warning is logged and it is tried again after 30 seconds. From 10 minutes before the expiration every caller waits for the
/// fetch, and a failure is thrown. If DynamoDB rejects the credentials as expired before that, a client drops them with
/// <see cref="TryInvalidate"/> and the next call fetches. Concurrent callers share one fetch, which a caller's cancellation does not cancel. The
/// provider is thread-safe.
/// </para>
/// <para>
/// Only the endpoint settings are read from the environment, at every fetch. Of the URI, messages and logs show the host alone,
/// because the rest of the URI of ECS contains a credential identifier and the host is no secret. The token never appears.
/// </para>
/// </remarks>
public sealed class ContainerCredentialsProvider : IRefreshableCredentialsProvider
{
    private const string RelativeUriVariable = "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI";
    private const string FullUriVariable = "AWS_CONTAINER_CREDENTIALS_FULL_URI";
    private const string TokenVariable = "AWS_CONTAINER_AUTHORIZATION_TOKEN";
    private const string TokenFileVariable = "AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE";
    private const string RelativeUriBase = "http://169.254.170.2";
    private const string SourceName = "container credentials endpoint";

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(2);
    private static readonly string[] AllowedHttpHosts = ["169.254.170.2", "169.254.170.23", "fd00:ec2::23", "localhost"];

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly ReadTextFile _readFile;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly RefreshingCredentialsCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerCredentialsProvider"/> class that reads the environment of the
    /// current process and uses the system clock.
    /// </summary>
    public ContainerCredentialsProvider()
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, null, TimeProvider.System, NullLogger.Instance)
    {
    }

    internal ContainerCredentialsProvider(
        Func<string, string?> getEnvironmentVariable,
        ReadTextFile readFile,
        HttpMessageHandler? handler,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _readFile = readFile;
        _http = CredentialsHttp.GetClient(handler);
        _timeProvider = timeProvider;
        _cache = new RefreshingCredentialsCache(SourceName, timeProvider, logger, FetchOutcomeAsync);
    }

    /// <summary>
    /// Gets the credentials, fetching or refreshing them when needed.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops this call from waiting. The fetch itself, which other callers may share, is not cancelled.
    /// </param>
    /// <returns>The same <see cref="AwsCredentials"/> instance until it is refreshed.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// Neither <c>AWS_CONTAINER_CREDENTIALS_RELATIVE_URI</c> nor <c>AWS_CONTAINER_CREDENTIALS_FULL_URI</c> is set; the
    /// configuration is invalid; or the endpoint does not return valid credentials. During the mandatory refresh period, the
    /// failure of a refresh is thrown as well.
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
        IsConfigured()
            ? _cache.GetAsync(cancellationToken)
            : ValueTask.FromResult(CredentialsOutcome.DoesNotApply($"neither {RelativeUriVariable} nor {FullUriVariable} is set"));

    private static bool IsAllowedHttpHost(string host) =>
        Array.IndexOf(AllowedHttpHosts, host) >= 0 || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static HttpRequestMessage CreateRequest(Uri endpoint, string? authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return request;
    }

    private bool IsConfigured() =>
        !string.IsNullOrEmpty(_getEnvironmentVariable(RelativeUriVariable)) || !string.IsNullOrEmpty(_getEnvironmentVariable(FullUriVariable));

    private Uri ResolveEndpoint()
    {
        if (_getEnvironmentVariable(RelativeUriVariable) is { Length: > 0 } relative)
        {
            return relative[0] == '/' && Uri.TryCreate(RelativeUriBase + relative, UriKind.Absolute, out var relativeEndpoint)
                ? relativeEndpoint
                : throw new CredentialsUnavailableException($"{RelativeUriVariable} must be a path that starts with '/'.");
        }

        // The value is not echoed: it may carry secrets in its user info or query.
        var full = _getEnvironmentVariable(FullUriVariable);
        if (!Uri.TryCreate(full, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            throw new CredentialsUnavailableException($"{FullUriVariable} must be an absolute http or https URL.");
        }

        if (endpoint.UserInfo.Length > 0)
        {
            throw new CredentialsUnavailableException($"{FullUriVariable} must not contain user info.");
        }

        if (endpoint.Scheme == Uri.UriSchemeHttp && !IsAllowedHttpHost(endpoint.DnsSafeHost))
        {
            throw new CredentialsUnavailableException(
                $"{FullUriVariable} points to the host '{endpoint.DnsSafeHost}', which is not allowed over http. " +
                "Use https, a loopback address, or one of 169.254.170.2, 169.254.170.23, fd00:ec2::23.");
        }

        return endpoint;
    }

    // The file wins over the variable; it is read at every fetch because EKS rotates it.
    private string? ReadAuthorizationToken()
    {
        string? token;
        if (_getEnvironmentVariable(TokenFileVariable) is { Length: > 0 } tokenFile)
        {
            token = ReadTokenFile(tokenFile).Trim();
            if (token.Length == 0)
            {
                throw new CredentialsUnavailableException($"The file named by {TokenFileVariable} is empty.");
            }
        }
        else
        {
            token = _getEnvironmentVariable(TokenVariable);
        }

        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        return token.AsSpan().ContainsAny('\r', '\n')
            ? throw new CredentialsUnavailableException("The authorization token must not contain line breaks.")
            : token;
    }

    private string ReadTokenFile(string path)
    {
        try
        {
            return _readFile(path) ?? throw new CredentialsUnavailableException($"The file named by {TokenFileVariable} does not exist.");
        }
        catch (Exception exception) when (SharedConfigFiles.IsFileReadFailure(exception))
        {
            throw new CredentialsUnavailableException(
                $"The file named by {TokenFileVariable} cannot be read: {SharedConfigFiles.DescribeFileReadFailure(exception)}.", exception);
        }
    }

    private async ValueTask<CredentialsOutcome> FetchOutcomeAsync() => CredentialsOutcome.Found(await FetchAsync().ConfigureAwait(false));

    // Credentials or an exception, and nothing that could say "does not apply": that was decided before the fetch, by the variables.
    private async ValueTask<AwsCredentials> FetchAsync()
    {
        var endpoint = ResolveEndpoint();
        var authorization = ReadAuthorizationToken();

        CredentialsDocument document = default;
        var parsed = false;
        CredentialsHttp.Verdict Judge(CredentialsHttp.Result result)
        {
            parsed = result.IsOk && CredentialsDocument.TryParse(result.Body, out document);
            return parsed ? CredentialsHttp.Verdict.Return : CredentialsHttp.Verdict.Retry;
        }

        var last = await CredentialsHttp.SendWithRetriesAsync(_http, () => CreateRequest(endpoint, authorization), Judge, AttemptTimeout, _timeProvider).ConfigureAwait(false);
        if (parsed)
        {
            return document.TryCreateCredentials(out var credentials) is { } problem
                ? throw new CredentialsUnavailableException($"The {SourceName} returned an unusable response: {problem}.")
                : credentials!;
        }

        var lastFailure = last.IsOk ? "the response is not a JSON object" : last.DescribeFailure();
        throw new CredentialsUnavailableException(
            $"The {SourceName} at {endpoint.DnsSafeHost} did not return credentials in {CredentialsHttp.MaxAttempts} attempts. The last attempt: {lastFailure}.");
    }
}
