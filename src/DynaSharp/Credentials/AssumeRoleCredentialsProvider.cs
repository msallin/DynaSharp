using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DynaSharp.Configuration;
using DynaSharp.Retries;
using DynaSharp.Signing;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies temporary credentials of an IAM role, which it gets from the <c>AssumeRole</c> call of AWS STS with the credentials of another
/// provider, and refreshes before they expire. This is what a profile with <c>role_arn</c> and <c>source_profile</c> or <c>credential_source</c>
/// does, see <see cref="ProfileCredentialsProvider"/>; this class is the same for code.
/// </summary>
/// <remarks>
/// <para>
/// A fetch asks the source for its credentials, signs a <c>POST</c> of a form with them (Signature Version 4, service <c>sts</c>) and sends it to
/// <c>https://sts.{region}.amazonaws.com/</c>, or <c>amazonaws.com.cn</c> for a region whose name starts with <c>cn-</c>. As in botocore,
/// <c>AWS_STS_REGIONAL_ENDPOINTS=legacy</c> selects the global endpoint <c>https://sts.amazonaws.com/</c> for the regions that used it before
/// regional endpoints existed, and <c>AWS_ENDPOINT_URL_STS</c>, then <c>AWS_ENDPOINT_URL</c>, go ahead of the region; <see cref="AssumeRoleOptions.StsEndpoint"/>
/// goes ahead of them all. The variables are read at every fetch. The region is <see cref="AssumeRoleOptions.Region"/>, or the one a client would
/// find, and a fetch that needs one and finds none fails.
/// </para>
/// <para>
/// Each request has 5 seconds. A timeout, a connection error, HTTP 429 and 5xx, the code <c>Throttling</c> and a successful response that is not XML are
/// retried up to 3 attempts in total, one second apart. Any other answer is permanent and throws a <see cref="CredentialsUnavailableException"/> that
/// names the role and the code, such as <c>AccessDenied</c> when the trust policy of the role does not let the source assume it. The <c>Message</c> of
/// an error is left out, as in <see cref="WebIdentityCredentialsProvider"/>. A source that has no credentials fails the fetch with its own message.
/// </para>
/// <para>
/// The same <see cref="AwsCredentials"/> instance is returned until it is refreshed, with the windows of <see cref="WebIdentityCredentialsProvider"/>:
/// in the background from 15 minutes before the expiration, by every caller from 10 minutes before it. Concurrent callers share one fetch. The provider
/// is thread-safe, and it does not dispose the source.
/// </para>
/// <para>
/// Multi-factor authentication is not supported: a session that needs a token code (<c>SerialNumber</c> and <c>TokenCode</c> of <c>AssumeRole</c>)
/// needs a person at every refresh. The requests do not go through <see cref="DynamoDbClientOptions.HttpHandler"/>, and use the default proxy of the
/// process unless the endpoint is on a loopback address.
/// </para>
/// </remarks>
public sealed class AssumeRoleCredentialsProvider : IRefreshableCredentialsProvider
{
    private const string FormContentType = "application/x-www-form-urlencoded";
    private const string ThrottlingCode = "Throttling";
    private const int MinDurationSeconds = 15 * 60;
    private const int MaxDurationSeconds = 12 * 60 * 60;

    private readonly ICredentialsProvider _source;
    private readonly Settings _settings;
    private readonly CredentialsDependencies _dependencies;
    private readonly ILogger _logger;
    private readonly RefreshingCredentialsCache _cache;
    private int _unencryptedWarned;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssumeRoleCredentialsProvider"/> class.
    /// </summary>
    /// <param name="sourceCredentials">The provider of the credentials that assume the role, for example a <see cref="DefaultCredentialsProvider"/>.</param>
    /// <param name="roleArn">The ARN of the role, for example <c>arn:aws:iam::123456789012:role/orders</c>.</param>
    /// <param name="roleSessionName">
    /// The name of the session, which shows in CloudTrail: 2 to 64 letters, digits or characters of <c>_+=,.@-</c>. <see langword="null"/>
    /// means <c>dynasharp-</c> and the Unix time in seconds at each fetch.
    /// </param>
    /// <param name="options">The external id, the duration, the region and the endpoint, or <see langword="null"/> for none of them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sourceCredentials"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="roleArn"/> is null or empty, <paramref name="roleSessionName"/> breaks the rule above, the external id is empty, the duration
    /// is not a whole number of seconds, or the endpoint is not valid.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The duration is shorter than 15 minutes or longer than 12 hours.</exception>
    public AssumeRoleCredentialsProvider(ICredentialsProvider sourceCredentials, string roleArn, string? roleSessionName = null, AssumeRoleOptions? options = null)
        : this(sourceCredentials ?? throw new ArgumentNullException(nameof(sourceCredentials)), Validate(roleArn, roleSessionName, options ?? new AssumeRoleOptions()), CredentialsDependencies.Default)
    {
    }

    internal AssumeRoleCredentialsProvider(ICredentialsProvider sourceCredentials, Settings settings, CredentialsDependencies dependencies)
    {
        _source = sourceCredentials;
        _settings = settings;
        _dependencies = dependencies;
        _logger = dependencies.LoggerFactory.CreateLogger<AssumeRoleCredentialsProvider>();
        _cache = new RefreshingCredentialsCache($"role {settings.RoleArn}", dependencies.TimeProvider, _logger, FetchAsync);
    }

    /// <summary>
    /// Gets the credentials of the role, fetching or refreshing them when needed.
    /// </summary>
    /// <param name="cancellationToken">Stops this call from waiting. The fetch itself, which other callers may share, is not cancelled.</param>
    /// <returns>The same <see cref="AwsCredentials"/> instance until it is refreshed.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// The source has no credentials, the region is needed and not known, or STS does not return valid credentials. During the mandatory refresh
    /// period, the failure of a refresh is thrown as well.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var outcome = await _cache.GetAsync(cancellationToken).ConfigureAwait(false);
        return outcome.GetCredentials($"role {_settings.RoleArn}");
    }

    /// <summary>
    /// Drops credentials that DynamoDB rejected as expired, so that the next <see cref="GetCredentialsAsync"/> assumes the role again.
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

    private static Settings Validate(string roleArn, string? roleSessionName, AssumeRoleOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(roleArn);
        if (roleSessionName is not null && !Sts.IsValidSessionName(roleSessionName))
        {
            throw new ArgumentException($"The session name {Sts.SessionNameRule}.", nameof(roleSessionName));
        }

        if (options.ExternalId is { Length: 0 })
        {
            throw new ArgumentException("The external id must not be empty; null sends none.", nameof(options));
        }

        int? durationSeconds = null;
        if (options.Duration is { } duration)
        {
            if (duration < TimeSpan.FromSeconds(MinDurationSeconds) || duration > TimeSpan.FromSeconds(MaxDurationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(options), duration, "The duration of the session must be 15 minutes to 12 hours.");
            }

            if (duration.Ticks % TimeSpan.TicksPerSecond != 0)
            {
                throw new ArgumentException("The duration of the session must be a whole number of seconds.", nameof(options));
            }

            durationSeconds = (int)duration.TotalSeconds;
        }

        // The value is not echoed: an endpoint may carry credentials in its user info.
        if (options.StsEndpoint is { } endpoint && ClientConfiguration.FindEndpointProblem(endpoint) is { } problem)
        {
            throw new ArgumentException($"The STS endpoint is not valid: {problem}.", nameof(options));
        }

        return new Settings(roleArn, roleSessionName, options.ExternalId, durationSeconds, options.Region, options.StsEndpoint);
    }

    private static bool IsTransient(CredentialsHttp.Result result) =>
        CredentialsHttp.IsTransientFailure(result)
        || (StsDocument.TryParse(result.Body, out var document) ? document.ErrorCode == ThrottlingCode : result.IsOk);

    // The Message of the error stays out, see StsDocument.
    private static string DescribeRejection(string? code, HttpStatusCode status) => code switch
    {
        "AccessDenied" => "AccessDenied: the trust policy of the role, or a policy of the source, does not allow the source to assume it",
        "ExpiredToken" => "ExpiredToken: the source credentials have expired",
        "InvalidClientTokenId" => "InvalidClientTokenId: STS does not know the access key of the source credentials",
        "SignatureDoesNotMatch" => "SignatureDoesNotMatch: the secret of the source credentials does not belong to its access key, or the local clock is off by more than five minutes",
        "RegionDisabledException" => "RegionDisabledException: STS is not activated in this region of the account",
        "ValidationError" => "ValidationError: a parameter is not valid, such as a duration beyond the maximum session duration of the role",
        null or "" => $"HTTP {(int)status}",
        _ => code,
    };

    // Input:  role arn:aws:iam::123456789012:role/r, session s, duration 3600, external id x
    // Result: Action=AssumeRole&Version=2011-06-15&RoleArn=arn%3Aaws%3Aiam%3A%3A123456789012%3Arole%2Fr&RoleSessionName=s&DurationSeconds=3600&ExternalId=x
    // The members follow the order of the shape in botocore's model, as botocore's serializer writes them.
    private string CreateBody()
    {
        var body = new StringBuilder("Action=AssumeRole&Version=2011-06-15&RoleArn=")
            .Append(Sts.Encode(_settings.RoleArn))
            .Append("&RoleSessionName=")
            .Append(Sts.Encode(_settings.SessionName ?? Sts.DefaultSessionName(_dependencies.TimeProvider)));
        if (_settings.DurationSeconds is { } seconds)
        {
            body.Append(CultureInfo.InvariantCulture, $"&DurationSeconds={seconds}");
        }

        if (_settings.ExternalId is { } externalId)
        {
            body.Append("&ExternalId=").Append(Sts.Encode(externalId));
        }

        return body.ToString();
    }

    // The global endpoint signs for us-east-1. A configured endpoint signs for the region that is known, and for us-east-1 without one, as an
    // emulator does not care and a VPC endpoint comes with the region of the client.
    private (Uri Endpoint, string SigningRegion) ResolveEndpoint()
    {
        var getEnvironmentVariable = _dependencies.GetEnvironmentVariable;
        var (endpoint, region) = Sts.ResolveEndpoint(getEnvironmentVariable, _settings.Endpoint, () => _settings.Region ?? Sts.ResolveRegion(getEnvironmentVariable, _dependencies.ReadFile));
        if (endpoint == Sts.GlobalEndpoint)
        {
            return (endpoint, "us-east-1");
        }

        return (endpoint, (region ?? _settings.Region ?? TryResolveRegion())?.Name ?? "us-east-1");
    }

    private AwsRegion? TryResolveRegion()
    {
        try
        {
            return Sts.ResolveRegion(_dependencies.GetEnvironmentVariable, _dependencies.ReadFile);
        }
        catch (CredentialsUnavailableException)
        {
            return null;
        }
    }

    private async ValueTask<AwsCredentials> GetSourceCredentialsAsync()
    {
        try
        {
            // The source bounds its own fetch, as every provider of this library does.
            return await _source.GetCredentialsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (CredentialsUnavailableException exception)
        {
            throw new CredentialsUnavailableException($"Assuming the role {_settings.RoleArn} needs the credentials of its source, and there are none: {exception.Message}", exception);
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The clients of CredentialsHttp are shared, and not to be disposed; SendWithRetriesAsync disposes each request.")]
    private async ValueTask<CredentialsOutcome> FetchAsync()
    {
        var (endpoint, signingRegion) = ResolveEndpoint();
        if (ClientConfiguration.IsUnencryptedRemote(endpoint) && Interlocked.Exchange(ref _unencryptedWarned, 1) == 0)
        {
            CredentialsLog.UnencryptedStsEndpoint(_logger, endpoint.Host);
        }

        var source = await GetSourceCredentialsAsync().ConfigureAwait(false);
        var body = CreateBody();
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        var signer = new SigV4Signer(signingRegion, "sts");
        var authority = SigV4Signer.GetAuthority(endpoint);
        var timeProvider = _dependencies.TimeProvider;
        var signedAt = default(DateTimeOffset);

        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                // Renders as "application/x-www-form-urlencoded; charset=utf-8", which the signer has as the content type of a form.
                Content = new StringContent(body, Encoding.UTF8, FormContentType),
            };
            signedAt = timeProvider.GetUtcNow();
            signer.SignForm(request, authority, payloadHash, source, signedAt);
            return request;
        }

        var http = CredentialsHttp.GetClientFor(endpoint, _dependencies.Handler);
        var clockCorrected = false;
        CredentialsHttp.Verdict Judge(CredentialsHttp.Result attempt)
        {
            if (!clockCorrected
                && attempt.Date is { } serverTime
                && StsDocument.TryParse(attempt.Body, out var rejection)
                && rejection.ErrorCode is { } code
                && ClockSkewCorrection.IsClockSkewCode(code)
                && timeProvider is ClockSkewCorrection clock
                && clock.TryCorrect(signedAt, serverTime, out var offset))
            {
                clockCorrected = true;
                CredentialsLog.ClockSkewCorrected(_logger, offset);
                return CredentialsHttp.Verdict.SendAgain;
            }

            return IsTransient(attempt) ? CredentialsHttp.Verdict.Retry : CredentialsHttp.Verdict.Return;
        }

        var result = await CredentialsHttp.SendWithRetriesAsync(http, CreateRequest, Judge, CredentialsHttp.AttemptTimeout, timeProvider).ConfigureAwait(false);
        StsDocument document = default;
        var parsed = result.Kind == CredentialsHttp.ResultKind.Response && StsDocument.TryParse(result.Body, out document);
        if (result.IsOk && parsed && document.ErrorCode is null)
        {
            return CredentialsOutcome.Found(document.CreateCredentials());
        }

        if (IsTransient(result))
        {
            var lastFailure = document.ErrorCode ?? (result.IsOk ? "the response is not XML" : result.DescribeFailure());
            throw new CredentialsUnavailableException(
                $"STS at {endpoint.DnsSafeHost} did not return credentials in {CredentialsHttp.MaxAttempts} attempts. The last attempt: {lastFailure}.");
        }

        throw new CredentialsUnavailableException($"STS did not let the source credentials assume the role {_settings.RoleArn}: {DescribeRejection(document.ErrorCode, result.StatusCode)}.");
    }

    /// <summary>What a fetch needs to know.</summary>
    /// <param name="RoleArn">The role to assume.</param>
    /// <param name="SessionName">The name of the session, or <see langword="null"/> for the default of each fetch.</param>
    /// <param name="ExternalId">The external id, or <see langword="null"/> for none.</param>
    /// <param name="DurationSeconds">The duration of the session, or <see langword="null"/> for the default of STS.</param>
    /// <param name="Region">The region of the endpoint and the signature, or <see langword="null"/> to look for it when it is needed.</param>
    /// <param name="Endpoint">The endpoint of STS that replaces all the others, or <see langword="null"/> to work it out at each fetch.</param>
    internal sealed record Settings(string RoleArn, string? SessionName, string? ExternalId, int? DurationSeconds, AwsRegion? Region, Uri? Endpoint);
}
