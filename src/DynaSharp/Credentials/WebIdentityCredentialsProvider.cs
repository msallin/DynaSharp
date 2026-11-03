using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using DynaSharp.Configuration;
using DynaSharp.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies temporary credentials of an IAM role for the holder of a web identity token: IAM roles for service accounts (IRSA) on
/// Amazon EKS, and any other OpenID Connect issuer that the account trusts, such as GitHub Actions. It trades the token for
/// credentials with the <c>AssumeRoleWithWebIdentity</c> call of AWS STS, and refreshes them before they expire.
/// </summary>
/// <remarks>
/// <para>
/// Without arguments the provider applies when <c>AWS_WEB_IDENTITY_TOKEN_FILE</c> is set and not empty; otherwise
/// <see cref="GetCredentialsAsync"/> throws a <see cref="CredentialsUnavailableException"/> that says so (the default chain then moves
/// on). <c>AWS_ROLE_ARN</c> must then be set as well: a token file without a role is broken and throws, which also stops the default
/// chain. <c>AWS_ROLE_SESSION_NAME</c> is optional, must be 2 to 64 letters, digits or characters of <c>_+=,.@-</c>, and defaults to
/// <c>dynasharp-</c> and the Unix time in seconds. The variables are read at every fetch. Passing the settings to a constructor replaces
/// them all. The profile keys <c>web_identity_token_file</c> and <c>role_arn</c> are not read: a profile that has them fails in
/// <see cref="ProfileCredentialsProvider"/>.
/// </para>
/// <para>
/// A fetch is a <c>POST</c> of a form to <c>https://sts.{region}.amazonaws.com/</c>, or <c>amazonaws.com.cn</c> for a region whose name starts
/// with <c>cn-</c>. It is not signed: the token is the proof. It asks for no duration, so the session lasts the default hour, which the maximum of the role can
/// never reject. The region is that of the client when a <see cref="DynamoDbClient"/> made the provider, and otherwise the one passed to the constructor, or else
/// <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c> or the <c>region</c> of the profile, and a fetch without any of these fails. As in botocore,
/// <c>AWS_STS_REGIONAL_ENDPOINTS=legacy</c> selects the global endpoint <c>https://sts.amazonaws.com/</c> for the regions that used it before regional
/// endpoints existed; other regions, such as those that must be enabled and those of China, keep their regional endpoint.
/// </para>
/// <para>
/// As in botocore, <c>AWS_ENDPOINT_URL_STS</c> and then <c>AWS_ENDPOINT_URL</c> go ahead of all of that: with either one set the fetch needs no region and <c>AWS_STS_REGIONAL_ENDPOINTS</c> counts for nothing. An empty
/// variable counts as unset, and a value takes what the endpoint of a client takes, an absolute http or https URL without user info, path, query or fragment; any other throws, and names the variable. So a process that
/// points <c>AWS_ENDPOINT_URL</c> at an emulator for DynamoDB sends the token there too, and over http the token travels in the clear; for a host other than a loopback address the provider logs a warning once. All of these variables are read from the environment at every fetch, also when the
/// token file, the role and the region were passed to the constructor. Only an endpoint passed to <see cref="WebIdentityCredentialsProvider(string, string, Uri, string?)"/> goes ahead of them: as for the token file and the role, an explicit
/// setting replaces the environment, so that provider reads no variable at all and needs no region. The profile settings <c>sts_regional_endpoints</c> and <c>endpoint_url</c>, the <c>services</c> sections of the shared config file and <c>AWS_IGNORE_CONFIGURED_ENDPOINT_URLS</c> are not read.
/// </para>
/// <para>
/// The token is read from the file for every request, because the kubelet replaces the file before the token expires; leading and trailing whitespace is
/// removed. A file that is missing, empty or unreadable throws. If STS answers <c>ExpiredTokenException</c>, the file is read again and the request is sent once more at
/// once, because the replacement may have arrived since. Each request has 5 seconds. A timeout, a connection error, HTTP 429 and 5xx, the codes <c>IDPCommunicationError</c> and
/// <c>Throttling</c>, and a successful response that is not XML are retried up to 3 attempts in total, one second apart, which keeps a failing fetch within the default
/// <see cref="DynamoDbClientOptions.AttemptTimeout"/> of a client. Any other answer is permanent and throws a <see cref="CredentialsUnavailableException"/>
/// that names the code: <c>InvalidIdentityToken</c>, <c>IDPRejectedClaim</c>, <c>AccessDenied</c> and whatever else STS may say. Once the
/// provider applies, the default chain never goes on to another source, so a pod never signs with the instance role of its node.
/// </para>
/// <para>
/// The requests use the default proxy of the process (<c>HTTPS_PROXY</c>, <c>NO_PROXY</c>), since STS is often reached through an egress proxy, unlike the link-local
/// endpoints of <see cref="ContainerCredentialsProvider"/>. An endpoint on a loopback address goes past the proxy, because a proxy cannot reach a port of this machine, and <c>HTTP_PROXY</c> would be asked all the same.
/// They go over https unless the endpoint says otherwise, and follow no redirects, so the token reaches no other host. Cookies are not used.
/// </para>
/// <para>
/// They do not go through <see cref="DynamoDbClientOptions.HttpHandler"/>, which carries the requests to DynamoDB and no others. A client with a stub handler in a process with <c>AWS_WEB_IDENTITY_TOKEN_FILE</c> set still
/// calls the real STS with that token: a test sets <see cref="DynamoDbClientOptions.Credentials"/>, or reaches <c>StubSts</c> of <c>DynaSharp.Testing</c>, a server on a port of this machine, by passing its endpoint to the constructor or by
/// pointing <c>AWS_ENDPOINT_URL_STS</c> at it.
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
/// The token is a bearer credential. It appears in no message, log or trace. The <c>Message</c> of an error of STS is left out of the exception for that reason: it is free text
/// that may quote the request. Look it up by the code, or in CloudTrail.
/// </para>
/// </remarks>
public sealed class WebIdentityCredentialsProvider : IRefreshableCredentialsProvider
{
    private const string TokenFileVariable = "AWS_WEB_IDENTITY_TOKEN_FILE";
    private const string RoleArnVariable = "AWS_ROLE_ARN";
    private const string SessionNameVariable = "AWS_ROLE_SESSION_NAME";
    private const string RegionalEndpointsVariable = "AWS_STS_REGIONAL_ENDPOINTS";
    private const string EndpointVariable = "AWS_ENDPOINT_URL_STS";
    private const string SourceName = "web identity token file";
    private const string FormContentType = "application/x-www-form-urlencoded";
    private const string SessionNameRule = "must be 2 to 64 letters, digits or characters of _+=,.@-";
    private const int MinSessionNameLength = 2;
    private const int MaxSessionNameLength = 64;
    private const int MaxAttempts = 3;

    // The codes are those of botocore's sts/2011-06-15/service-2.json, which has ExpiredTokenException with that suffix on the wire. Throttling
    // and AccessDenied are no modeled errors of this operation, yet STS sends them: botocore's retries/standard.py and _retry.json list the former.
    private const string ExpiredTokenCode = "ExpiredTokenException";
    private const string CommunicationErrorCode = "IDPCommunicationError";
    private const string ThrottlingCode = "Throttling";

    // An issuer that is slow to serve its keys makes STS itself take seconds. 3 attempts, 2 pauses and the one resend of an expired token stay below
    // the 30 seconds that a client gives an attempt by default, so that a client reports why the fetch failed and not a timeout of its own.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly Uri GlobalEndpoint = new("https://sts.amazonaws.com/");

    // botocore's LEGACY_GLOBAL_STS_REGIONS, in args.py.
    private static readonly string[] LegacyGlobalRegions =
    [
        "ap-northeast-1", "ap-south-1", "ap-southeast-1", "ap-southeast-2", "ca-central-1", "eu-central-1", "eu-north-1", "eu-west-1",
        "eu-west-2", "eu-west-3", "sa-east-1", "us-east-1", "us-east-2", "us-west-1", "us-west-2",
    ];

    // The pattern of roleSessionNameType is [\w+=,.@-]*, where \w means ASCII; a .NET \w would let other letters through.
    private static readonly SearchValues<char> SessionNameCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_+=,.@-");

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly ReadTextFile _readFile;
    private readonly HttpMessageHandler? _handler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly AwsRegion? _region;
    private readonly Settings? _settings;
    private readonly RefreshingCredentialsCache _cache;
    private int _unencryptedWarned;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebIdentityCredentialsProvider"/> class that reads its settings from the environment of the
    /// current process, reads the token from disk and uses the system clock.
    /// </summary>
    public WebIdentityCredentialsProvider()
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, null, TimeProvider.System, NullLogger.Instance, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebIdentityCredentialsProvider"/> class with explicit settings. The variables
    /// <c>AWS_WEB_IDENTITY_TOKEN_FILE</c>, <c>AWS_ROLE_ARN</c> and <c>AWS_ROLE_SESSION_NAME</c> are ignored.
    /// </summary>
    /// <param name="tokenFile">The path of the file that holds the token. It is read at every fetch.</param>
    /// <param name="roleArn">The ARN of the role to assume, for example <c>arn:aws:iam::123456789012:role/orders</c>.</param>
    /// <param name="region">The region of the STS endpoint.</param>
    /// <param name="roleSessionName">
    /// The name of the session, which shows in CloudTrail: 2 to 64 letters, digits or characters of <c>_+=,.@-</c>. <see langword="null"/>
    /// means <c>dynasharp-</c> and the Unix time in seconds at each fetch.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tokenFile"/> or <paramref name="roleArn"/> is null or empty, or <paramref name="roleSessionName"/> breaks the rule above.
    /// </exception>
    public WebIdentityCredentialsProvider(string tokenFile, string roleArn, AwsRegion region, string? roleSessionName = null)
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, null, TimeProvider.System, NullLogger.Instance, null, Validate(tokenFile, roleArn, region, roleSessionName))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebIdentityCredentialsProvider"/> class with explicit settings and an endpoint of its own. It reads no variable of the environment:
    /// the endpoint goes ahead of <c>AWS_ENDPOINT_URL_STS</c>, <c>AWS_ENDPOINT_URL</c>, the region and <c>AWS_STS_REGIONAL_ENDPOINTS</c>, which is what the constructor with a region does not do.
    /// </summary>
    /// <param name="tokenFile">The path of the file that holds the token. It is read at every fetch.</param>
    /// <param name="roleArn">The ARN of the role to assume, for example <c>arn:aws:iam::123456789012:role/orders</c>.</param>
    /// <param name="stsEndpoint">
    /// The endpoint of STS, for example a VPC endpoint or <c>StubSts.Endpoint</c> of <c>DynaSharp.Testing</c>: an absolute http or https URL without user info, path, query or fragment.
    /// Over http the token travels in the clear.
    /// </param>
    /// <param name="roleSessionName">
    /// The name of the session, which shows in CloudTrail: 2 to 64 letters, digits or characters of <c>_+=,.@-</c>. <see langword="null"/>
    /// means <c>dynasharp-</c> and the Unix time in seconds at each fetch.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="stsEndpoint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tokenFile"/> or <paramref name="roleArn"/> is null or empty, or <paramref name="stsEndpoint"/> or <paramref name="roleSessionName"/> breaks the rule above.
    /// </exception>
    public WebIdentityCredentialsProvider(string tokenFile, string roleArn, Uri stsEndpoint, string? roleSessionName = null)
        : this(ProcessEnvironment.GetVariable, SharedConfigFiles.DiskReader, null, TimeProvider.System, NullLogger.Instance, null, Validate(tokenFile, roleArn, stsEndpoint, roleSessionName))
    {
    }

    internal WebIdentityCredentialsProvider(
        Func<string, string?> getEnvironmentVariable,
        ReadTextFile readFile,
        HttpMessageHandler? handler,
        TimeProvider timeProvider,
        ILogger logger,
        AwsRegion? region)
        : this(getEnvironmentVariable, readFile, handler, timeProvider, logger, region, null)
    {
    }

    internal WebIdentityCredentialsProvider(
        Func<string, string?> getEnvironmentVariable,
        ReadTextFile readFile,
        HttpMessageHandler? handler,
        TimeProvider timeProvider,
        ILogger logger,
        AwsRegion? region,
        Settings? settings)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _readFile = readFile;
        _handler = handler;
        _timeProvider = timeProvider;
        _logger = logger;
        _region = region;
        _settings = settings;
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
    /// The provider was created without settings and <c>AWS_WEB_IDENTITY_TOKEN_FILE</c> is not set; a setting is missing or invalid; the
    /// token file cannot be read; or STS does not return valid credentials. During the mandatory refresh period, the failure of a refresh is
    /// thrown as well.
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
        _settings is not null || !string.IsNullOrEmpty(_getEnvironmentVariable(TokenFileVariable))
            ? _cache.GetAsync(cancellationToken)
            : ValueTask.FromResult(CredentialsOutcome.DoesNotApply($"{TokenFileVariable} is not set"));

    private static Settings Validate(string tokenFile, string roleArn, AwsRegion region, string? roleSessionName)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Validate(tokenFile, roleArn, roleSessionName) with { Region = region };
    }

    private static Settings Validate(string tokenFile, string roleArn, Uri stsEndpoint, string? roleSessionName)
    {
        ArgumentNullException.ThrowIfNull(stsEndpoint);

        // The value is not echoed, as for the variables: an endpoint may carry credentials in its user info.
        if (ClientConfiguration.FindEndpointProblem(stsEndpoint) is { } problem)
        {
            throw new ArgumentException($"The STS endpoint is not valid: {problem}.", nameof(stsEndpoint));
        }

        return Validate(tokenFile, roleArn, roleSessionName) with { Endpoint = stsEndpoint };
    }

    private static Settings Validate(string tokenFile, string roleArn, string? roleSessionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenFile);
        ArgumentException.ThrowIfNullOrEmpty(roleArn);
        if (roleSessionName is not null && !IsValidSessionName(roleSessionName))
        {
            throw new ArgumentException($"The session name {SessionNameRule}.", nameof(roleSessionName));
        }

        return new Settings(tokenFile, roleArn, null, roleSessionName);
    }

    private static bool IsValidSessionName(string name) =>
        name.Length is >= MinSessionNameLength and <= MaxSessionNameLength && !name.AsSpan().ContainsAnyExcept(SessionNameCharacters);

    // What botocore's urlencode does, so the body is the same byte for byte: RFC 3986 percent encoding, and a space as '+'.
    // Input: "arn:aws:iam::1:role/a b"   Result: "arn%3Aaws%3Aiam%3A%3A1%3Arole%2Fa+b"
    private static string Encode(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);

    private static HttpRequestMessage CreateRequest(Uri endpoint, string body) => new(HttpMethod.Post, endpoint)
    {
        // Renders as "application/x-www-form-urlencoded; charset=utf-8".
        Content = new StringContent(body, Encoding.UTF8, FormContentType),
    };

    private static bool IsTransient(CredentialsHttp.Result result, bool parsed, string? code) =>
        result.Kind != CredentialsHttp.ResultKind.Response
        || (int)result.StatusCode >= 500
        || result.StatusCode == HttpStatusCode.TooManyRequests
        || code is CommunicationErrorCode or ThrottlingCode
        || (result.IsOk && !parsed);

    private static AwsCredentials CreateCredentials(StsDocument document) =>
        document.Credentials.TryCreateCredentials(out var credentials, StsDocument.SessionTokenElement) is { } problem
            ? throw new CredentialsUnavailableException($"STS returned an unusable response: {problem}.")
            : credentials!;

    // The Message of the error stays out, see StsDocument.
    private static string DescribeRejection(string? code, HttpStatusCode status) => code switch
    {
        "InvalidIdentityToken" => "InvalidIdentityToken: the token is malformed or its signature is wrong, or the account has no OIDC provider for its issuer",
        "IDPRejectedClaim" => "IDPRejectedClaim: the identity provider does not accept a claim of the token, usually its audience or subject",
        "AccessDenied" => "AccessDenied: the trust policy of the role does not allow this token to assume it",
        null or "" => $"HTTP {(int)status}",
        _ => code,
    };

    private Settings ResolveSettings()
    {
        if (_settings is { } settings)
        {
            return settings;
        }

        var tokenFile = _getEnvironmentVariable(TokenFileVariable)!;
        var roleArn = _getEnvironmentVariable(RoleArnVariable);
        if (string.IsNullOrEmpty(roleArn))
        {
            throw new CredentialsUnavailableException(
                $"{TokenFileVariable} is set but {RoleArnVariable} is not: web identity federation needs the role to assume. " +
                $"Set {RoleArnVariable}, or pass credentials in the client options.");
        }

        var sessionName = _getEnvironmentVariable(SessionNameVariable);
        if (string.IsNullOrEmpty(sessionName))
        {
            sessionName = null;
        }
        else if (!IsValidSessionName(sessionName))
        {
            throw new CredentialsUnavailableException($"{SessionNameVariable} {SessionNameRule}.");
        }

        return new Settings(tokenFile, roleArn, _region, sessionName);
    }

    // Only a provider that no client made gets here, without an endpoint of its own. Without a region there is no regional endpoint, and no reason to pick another one silently.
    private AwsRegion ResolveRegion()
    {
        try
        {
            return ClientConfiguration.ResolveRegion(new DynamoDbClientOptions(), _getEnvironmentVariable, _readFile).Region;
        }
        catch (InvalidOperationException exception)
        {
            throw new CredentialsUnavailableException($"The STS endpoint depends on the region, and it is not known: {exception.Message}", exception);
        }
    }

    // What botocore does: a configured endpoint goes ahead of the region and of the legacy mode, and with one the region does not matter, because the request is not signed.
    // An endpoint passed in code is botocore's endpoint_url, which goes ahead of the configured ones.
    private Uri ResolveEndpoint(Settings settings)
    {
        if ((settings.Endpoint ?? ReadConfiguredEndpoint()) is { } configured)
        {
            return configured;
        }

        var region = settings.Region ?? ResolveRegion();
        return string.Equals(_getEnvironmentVariable(RegionalEndpointsVariable), "legacy", StringComparison.OrdinalIgnoreCase) && Array.IndexOf(LegacyGlobalRegions, region.Name) >= 0
            ? GlobalEndpoint
            : region.StsEndpoint;
    }

    private Uri? ReadConfiguredEndpoint()
    {
        try
        {
            return ClientConfiguration.FindEndpointInEnvironment(_getEnvironmentVariable, EndpointVariable).Endpoint;
        }
        catch (InvalidOperationException exception)
        {
            throw new CredentialsUnavailableException(exception.Message, exception);
        }
    }

    private string ReadToken(string path)
    {
        string? text;
        try
        {
            text = _readFile(path);
        }
        catch (Exception exception) when (SharedConfigFiles.IsFileReadFailure(exception))
        {
            throw new CredentialsUnavailableException(
                $"The {SourceName} cannot be read: {SharedConfigFiles.DescribeFileReadFailure(exception)}.", exception);
        }

        var token = text?.Trim() ?? throw new CredentialsUnavailableException($"The {SourceName} does not exist.");
        return token.Length > 0 ? token : throw new CredentialsUnavailableException($"The {SourceName} is empty.");
    }

    private string CreateBody(Settings settings)
    {
        var sessionName = settings.SessionName ?? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"dynasharp-{_timeProvider.GetUtcNow().ToUnixTimeSeconds()}");
        return $"Action=AssumeRoleWithWebIdentity&Version=2011-06-15&RoleArn={Encode(settings.RoleArn)}&RoleSessionName={Encode(sessionName)}&WebIdentityToken={Encode(ReadToken(settings.TokenFile))}";
    }

    private async ValueTask<CredentialsOutcome> FetchOutcomeAsync() => CredentialsOutcome.Found(await FetchAsync().ConfigureAwait(false));

    // Credentials or an exception, and nothing that could say "does not apply": that was decided before the fetch, by the variable.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The clients of CredentialsHttp are shared, and not to be disposed.")]
    private async ValueTask<AwsCredentials> FetchAsync()
    {
        var settings = ResolveSettings();
        var endpoint = ResolveEndpoint(settings);
        if (ClientConfiguration.IsUnencryptedRemote(endpoint) && Interlocked.Exchange(ref _unencryptedWarned, 1) == 0)
        {
            CredentialsLog.UnencryptedStsEndpoint(_logger, endpoint.Host);
        }

        var http = CredentialsHttp.GetClientFor(endpoint, _handler);

        var failedAttempts = 0;
        var expiredTokenSeen = false;
        while (true)
        {
            using var request = CreateRequest(endpoint, CreateBody(settings));
            var result = await CredentialsHttp.SendAsync(http, request, AttemptTimeout, _timeProvider).ConfigureAwait(false);

            StsDocument document = default;
            var parsed = result.Kind == CredentialsHttp.ResultKind.Response && StsDocument.TryParse(result.Body, out document);
            var code = document.ErrorCode;
            if (result.IsOk && parsed && code is null)
            {
                return CreateCredentials(document);
            }

            if (code == ExpiredTokenCode && !expiredTokenSeen)
            {
                expiredTokenSeen = true;
                CredentialsLog.WebIdentityTokenExpired(_logger, SourceName);
                continue;
            }

            if (code == ExpiredTokenCode)
            {
                throw new CredentialsUnavailableException(
                    $"STS rejected the web identity token with {ExpiredTokenCode}, also after the file was read again: whatever renews the file is behind or has stopped.");
            }

            if (!IsTransient(result, parsed, code))
            {
                throw new CredentialsUnavailableException($"STS rejected the web identity token with {DescribeRejection(code, result.StatusCode)}.");
            }

            failedAttempts++;
            if (failedAttempts == MaxAttempts)
            {
                var lastFailure = code ?? (result.IsOk ? "the response is not XML" : result.DescribeFailure());
                throw new CredentialsUnavailableException(
                    $"STS at {endpoint.DnsSafeHost} did not return credentials in {MaxAttempts} attempts. The last attempt: {lastFailure}.");
            }

            await Task.Delay(RetryDelay, _timeProvider).ConfigureAwait(false);
        }
    }

    /// <summary>What a fetch needs to know, however it came about.</summary>
    /// <param name="TokenFile">The path of the token file.</param>
    /// <param name="RoleArn">The role to assume.</param>
    /// <param name="Region">The region of the STS endpoint, or <see langword="null"/> to look for it when an endpoint depends on it.</param>
    /// <param name="SessionName">The name of the session, or <see langword="null"/> for the default of each fetch.</param>
    /// <param name="Endpoint">The endpoint of STS that replaces all the others, or <see langword="null"/> to work it out at each fetch.</param>
    internal sealed record Settings(string TokenFile, string RoleArn, AwsRegion? Region, string? SessionName, Uri? Endpoint = null);
}
