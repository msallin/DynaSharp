using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DynaSharp.Configuration;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies the credentials of a role in an account that AWS IAM Identity Center (SSO) gives the user who signed in with <c>aws sso login</c>, from
/// the token that the AWS CLI caches, and refreshes them before they expire. This is what a profile with <c>sso_account_id</c> and
/// <c>sso_role_name</c> does, see <see cref="ProfileCredentialsProvider"/>; this class is the same for code.
/// </summary>
/// <remarks>
/// <para>
/// A fetch reads the cached token from <c>~/.aws/sso/cache/</c>, from the file named by the SHA-1 of the session name in hexadecimal, or of the start
/// URL when there is no session (the older form), as botocore does, and sends it as a bearer token to the <c>GetRoleCredentials</c> call of the portal,
/// <c>GET https://portal.sso.{region}.amazonaws.com/federation/credentials?role_name=...&amp;account_id=...</c>. The file is read at every fetch, so a new
/// <c>aws sso login</c> takes effect at the next refresh. <c>AWS_ENDPOINT_URL_SSO</c>, then <c>AWS_ENDPOINT_URL</c>, replace the endpoint, as in botocore.
/// </para>
/// <para>
/// With a session, a token that expires within 15 minutes is refreshed first through the <c>CreateToken</c> call of the OIDC service
/// (<c>https://oidc.{region}.amazonaws.com/token</c>, or <c>AWS_ENDPOINT_URL_SSO_OIDC</c>) with the refresh token and the client registration that
/// the file holds, and the new token is written back into the file, as botocore does, so that the AWS CLI sees it too. A refresh that fails is
/// logged as a warning and the cached token is used while it lasts. A token without a session is never refreshed.
/// </para>
/// <para>
/// A token that is missing, unreadable, malformed or expired, and one that the portal rejects (HTTP 401), fail with a
/// <see cref="CredentialsUnavailableException"/> that says to run <c>aws sso login</c>. Each request has 5 seconds; a timeout, a connection error and
/// HTTP 429 and 5xx are retried up to 3 attempts in total, one second apart. The credentials are refreshed with the windows of
/// <see cref="WebIdentityCredentialsProvider"/>, and the provider is thread-safe. The tokens are secrets: they appear in no message, log or trace.
/// The requests do not go through <see cref="DynamoDbClientOptions.HttpHandler"/>, and use the default proxy of the process.
/// </para>
/// </remarks>
public sealed class SsoCredentialsProvider : IRefreshableCredentialsProvider
{
    private const string PortalEndpointVariable = "AWS_ENDPOINT_URL_SSO";
    private const string OidcEndpointVariable = "AWS_ENDPOINT_URL_SSO_OIDC";
    private const string BearerTokenHeader = "x-amz-sso_bearer_token";

    // botocore's SSOTokenProvider._REFRESH_WINDOW.
    private static readonly TimeSpan TokenRefreshWindow = TimeSpan.FromMinutes(15);

    // One shared delegate for the two calls, so that no call site converts a method group.
    private static readonly Func<CredentialsHttp.Result, bool> IsTransient = CredentialsHttp.IsTransientFailure;

    private readonly Settings _settings;
    private readonly CredentialsDependencies _dependencies;
    private readonly ILogger _logger;
    private readonly RefreshingCredentialsCache _cache;
    private int _unencryptedWarned;

    /// <summary>Initializes a new instance of the <see cref="SsoCredentialsProvider"/> class.</summary>
    /// <param name="startUrl">The start URL of the portal, for example <c>https://my-sso-portal.awsapps.com/start</c>: <c>sso_start_url</c>.</param>
    /// <param name="ssoRegion">The region of IAM Identity Center: <c>sso_region</c>.</param>
    /// <param name="accountId">The account of the role, for example <c>123456789012</c>: <c>sso_account_id</c>.</param>
    /// <param name="roleName">The name of the permission set, for example <c>ReadOnly</c>: <c>sso_role_name</c>.</param>
    /// <param name="sessionName">
    /// The name of the <c>sso-session</c> that <c>aws sso login --sso-session</c> signed in with, which names the cached token and lets it be refreshed.
    /// <see langword="null"/> for the older form, whose token is named by the start URL and expires without a refresh.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ssoRegion"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="startUrl"/>, <paramref name="accountId"/> or <paramref name="roleName"/> is null or empty, or <paramref name="sessionName"/> is empty.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "The start URL names the cached token by its SHA-1 exactly as written, which a Uri would normalize.")]
    public SsoCredentialsProvider(string startUrl, AwsRegion ssoRegion, string accountId, string roleName, string? sessionName = null)
        : this(Validate(startUrl, ssoRegion, accountId, roleName, sessionName), CredentialsDependencies.Default)
    {
    }

    internal SsoCredentialsProvider(Settings settings, CredentialsDependencies dependencies)
    {
        _settings = settings;
        _dependencies = dependencies;
        _logger = dependencies.LoggerFactory.CreateLogger<SsoCredentialsProvider>();
        _cache = new RefreshingCredentialsCache(SourceName, dependencies.TimeProvider, _logger, FetchAsync);
    }

    private string SourceName => $"SSO role {_settings.RoleName} in the account {_settings.AccountId}";

    // The session, or the start URL of the older form: what names the token, in messages.
    private string TokenName => _settings.SessionName is { } session ? $"the sso-session '{session}'" : $"the start URL {_settings.StartUrl}";

    /// <summary>Gets the credentials of the role, fetching or refreshing them when needed.</summary>
    /// <param name="cancellationToken">Stops this call from waiting. The fetch itself, which other callers may share, is not cancelled.</param>
    /// <returns>The same <see cref="AwsCredentials"/> instance until it is refreshed.</returns>
    /// <exception cref="CredentialsUnavailableException">
    /// There is no valid cached token (the message says to run <c>aws sso login</c>), or the portal does not return credentials. During the mandatory
    /// refresh period, the failure of a refresh is thrown as well.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var outcome = await _cache.GetAsync(cancellationToken).ConfigureAwait(false);
        return outcome.GetCredentials(SourceName);
    }

    /// <summary>Drops credentials that DynamoDB rejected as expired, so that the next <see cref="GetCredentialsAsync"/> asks the portal again. A client does this by itself.</summary>
    /// <param name="rejected">The instance that was rejected. Any other than the current one has been replaced already, and is ignored.</param>
    /// <returns>Always <see langword="true"/>: this source can be asked again.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rejected"/> is <see langword="null"/>.</exception>
    public bool TryInvalidate(AwsCredentials rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        _cache.Drop(rejected);
        return true;
    }

    private static Settings Validate(string startUrl, AwsRegion ssoRegion, string accountId, string roleName, string? sessionName)
    {
        ArgumentException.ThrowIfNullOrEmpty(startUrl);
        ArgumentNullException.ThrowIfNull(ssoRegion);
        ArgumentException.ThrowIfNullOrEmpty(accountId);
        ArgumentException.ThrowIfNullOrEmpty(roleName);
        if (sessionName is { Length: 0 })
        {
            throw new ArgumentException("The session name must not be empty; null means the older form without a session.", nameof(sessionName));
        }

        var signIn = sessionName is null
            ? $"Run `aws sso login` with a profile whose sso_start_url is {startUrl} to sign in again."
            : $"Run `aws sso login --sso-session {sessionName}` to sign in again.";
        return new Settings(startUrl, ssoRegion, accountId, roleName, sessionName, signIn);
    }

    // The body of CreateToken, in the order of botocore's serializer: {"grantType": "refresh_token", "clientId": ..., "clientSecret": ..., "refreshToken": ...}
    private static string CreateTokenBody(SsoToken token)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("grantType", "refresh_token");
            writer.WriteString("clientId", token.ClientId);
            writer.WriteString("clientSecret", token.ClientSecret);
            writer.WriteString("refreshToken", token.RefreshToken);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // Input:  {"roleCredentials":{"accessKeyId":"ASIA...","secretAccessKey":"...","sessionToken":"...","expiration":1700000000000}}
    // Result: the credentials, with the expiration in milliseconds since 1970 as botocore's _parse_timestamp reads it.
    private static string? TryParseRoleCredentials(byte[] json, out AwsCredentials? credentials)
    {
        credentials = null;
        string? accessKeyId = null, secretAccessKey = null, sessionToken = null;
        long? expiration = null;
        var reader = new Utf8JsonReader(json);
        try
        {
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return "the response is not a JSON object";
            }

            for (reader.Read(); reader.TokenType == JsonTokenType.PropertyName; reader.Read())
            {
                var outer = reader.ValueTextEquals("roleCredentials"u8);
                reader.Read();
                if (!outer || reader.TokenType != JsonTokenType.StartObject)
                {
                    reader.Skip();
                    continue;
                }

                for (reader.Read(); reader.TokenType == JsonTokenType.PropertyName; reader.Read())
                {
                    var name = reader.GetString();
                    reader.Read();
                    switch (name)
                    {
                        case "accessKeyId" when reader.TokenType == JsonTokenType.String:
                            accessKeyId = reader.GetString();
                            break;
                        case "secretAccessKey" when reader.TokenType == JsonTokenType.String:
                            secretAccessKey = reader.GetString();
                            break;
                        case "sessionToken" when reader.TokenType == JsonTokenType.String:
                            sessionToken = reader.GetString();
                            break;
                        case "expiration" when reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var milliseconds):
                            expiration = milliseconds;
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // The exception may quote the response, which holds the secret. InvalidOperationException: a string that is not valid UTF-8.
            return "the response is not a JSON object";
        }

        var missing = new List<string>(4);
        if (string.IsNullOrEmpty(accessKeyId))
        {
            missing.Add("accessKeyId");
        }

        if (string.IsNullOrEmpty(secretAccessKey))
        {
            missing.Add("secretAccessKey");
        }

        if (string.IsNullOrEmpty(sessionToken))
        {
            missing.Add("sessionToken");
        }

        if (expiration is not { } value || value < DateTimeOffset.MinValue.ToUnixTimeMilliseconds() || value > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            missing.Add("expiration");
        }

        if (missing.Count > 0)
        {
            return "the response does not contain roleCredentials with " + string.Join(", ", missing);
        }

        credentials = new AwsCredentials(accessKeyId!, secretAccessKey!, sessionToken, DateTimeOffset.FromUnixTimeMilliseconds(expiration!.Value));
        return null;
    }

    // Input:  {"accessToken":"...","tokenType":"Bearer","expiresIn":28800,"refreshToken":"..."}
    // Result: the access token, its lifetime in seconds and the refresh token if there is one; null if one of the first two is missing.
    private static (string AccessToken, int ExpiresIn, string? RefreshToken)? TryParseCreatedToken(byte[] json)
    {
        string? accessToken = null, refreshToken = null;
        int? expiresIn = null;
        var reader = new Utf8JsonReader(json);
        try
        {
            // A name comes only after the brace of an object, so a document that is no object ends the loop at once.
            reader.Read();
            for (reader.Read(); reader.TokenType == JsonTokenType.PropertyName; reader.Read())
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "accessToken" when reader.TokenType == JsonTokenType.String:
                        accessToken = reader.GetString();
                        break;
                    case "refreshToken" when reader.TokenType == JsonTokenType.String:
                        refreshToken = reader.GetString();
                        break;
                    case "expiresIn" when reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var seconds):
                        expiresIn = seconds;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }

        return string.IsNullOrEmpty(accessToken) || expiresIn is not { } lifetime ? null : (accessToken, lifetime, refreshToken);
    }

    // The code of an error of the OIDC service, such as invalid_grant, which says why without the description, which may quote the request.
    private static string? ReadOidcError(byte[] json)
    {
        // A body of a result without a response is empty, which is no JSON either.
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The status codes of the errors of GetRoleCredentials in botocore's sso/2019-06-10/service-2.json, and 403, which the portal sends for a role it does not give the user.
    private string DescribeRejection(HttpStatusCode status) => (int)status switch
    {
        400 => "InvalidRequestException: the portal refused the request",
        401 => $"UnauthorizedException: the token of {TokenName} has expired or was revoked. {_settings.SignIn}",
        403 => $"ForbiddenException: the user may not use the role {_settings.RoleName} in the account {_settings.AccountId}",
        404 => $"ResourceNotFoundException: the user has no role {_settings.RoleName} in the account {_settings.AccountId}",
        _ => string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)status}"),
    };

    private CredentialsUnavailableException LoginNeeded(string problem) =>
        new($"The cached SSO token of {TokenName} {problem}. {_settings.SignIn}");

    private Uri ResolveEndpoint(string variable, Uri regional)
    {
        Uri endpoint;
        try
        {
            endpoint = ClientConfiguration.FindEndpointInEnvironment(_dependencies.GetEnvironmentVariable, variable).Endpoint ?? regional;
        }
        catch (InvalidOperationException exception)
        {
            throw new CredentialsUnavailableException(exception.Message, exception);
        }

        if (ClientConfiguration.IsUnencryptedRemote(endpoint) && Interlocked.Exchange(ref _unencryptedWarned, 1) == 0)
        {
            CredentialsLog.UnencryptedSsoEndpoint(_logger, endpoint.Host);
        }

        return endpoint;
    }

    // botocore's SSOTokenLoader: the file is named by the SHA-1 of the session name, or of the start URL without a session.
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "Not a protection: SHA-1 names the cache file, as botocore and the AWS CLI name it.")]
    private string TokenPath()
    {
        var key = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(_settings.SessionName ?? _settings.StartUrl)));
        return Path.Combine(SharedConfigFiles.FindHomeDirectory(_dependencies.GetEnvironmentVariable), ".aws", "sso", "cache", key + ".json");
    }

    private (string Path, SsoToken Token, DateTimeOffset ExpiresAt) LoadToken()
    {
        var path = TokenPath();
        string? text;
        try
        {
            text = _dependencies.ReadFile(path);
        }
        catch (Exception exception) when (SharedConfigFiles.IsFileReadFailure(exception))
        {
            throw new CredentialsUnavailableException(
                $"The cached SSO token of {TokenName} cannot be read: {SharedConfigFiles.DescribeFileReadFailure(exception)}.", exception);
        }

        if (text is null)
        {
            throw LoginNeeded("does not exist");
        }

        var token = SsoToken.Parse(text);
        return token is { AccessToken.Length: > 0 } && SsoToken.ParseTime(token.ExpiresAt) is { } expiresAt
            ? (path, token, expiresAt)
            : throw LoginNeeded("is not valid");
    }

    // botocore's SSOTokenProvider._refresh_access_token: null when the file holds no registration, it has expired, or CreateToken fails.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The clients of CredentialsHttp are shared; SendWithRetriesAsync disposes each request.")]
    private async ValueTask<(SsoToken Token, DateTimeOffset ExpiresAt)?> TryRefreshTokenAsync(string path, SsoToken token)
    {
        var now = _dependencies.TimeProvider.GetUtcNow();
        if (token.RefreshToken is null || token.ClientId is null || token.ClientSecret is null || SsoToken.ParseTime(token.RegistrationExpiresAt) is not { } registrationExpiresAt)
        {
            return null;
        }

        if (registrationExpiresAt <= now)
        {
            CredentialsLog.SsoTokenRefreshFailed(_logger, TokenName, "the registration of the client has expired");
            return null;
        }

        var body = CreateTokenBody(token);
        var endpoint = new Uri(ResolveEndpoint(OidcEndpointVariable, _settings.Region.SsoOidcEndpoint), "token");
        HttpRequestMessage CreateRequest() => new(HttpMethod.Post, endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        var http = CredentialsHttp.GetClientFor(endpoint, _dependencies.Handler);
        var result = await CredentialsHttp.SendWithRetriesAsync(http, CreateRequest, IsTransient, _dependencies.TimeProvider).ConfigureAwait(false);
        var created = result.IsOk ? TryParseCreatedToken(result.Body) : null;
        if (created is not { } fresh)
        {
            var reason = result.DescribeFailure();
            if (result.IsOk)
            {
                reason = "the response does not contain accessToken and expiresIn";
            }
            else if (ReadOidcError(result.Body) is { } error)
            {
                reason = string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)result.StatusCode} {error}");
            }

            CredentialsLog.SsoTokenRefreshFailed(_logger, TokenName, reason);
            return null;
        }

        var expiresAt = _dependencies.TimeProvider.GetUtcNow().AddSeconds(fresh.ExpiresIn);
        var refreshed = new SsoToken(
            _settings.StartUrl,
            _settings.Region.Name,
            fresh.AccessToken,
            expiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            token.ClientId,
            token.ClientSecret,
            token.RegistrationExpiresAt,
            fresh.RefreshToken);
        try
        {
            _dependencies.WriteFile(path, refreshed.ToJson());
            CredentialsLog.SsoTokenRefreshed(_logger, TokenName);
        }
        catch (Exception exception) when (SharedConfigFiles.IsFileReadFailure(exception))
        {
            // The new token works for this fetch all the same; the next one refreshes again.
            CredentialsLog.SsoTokenNotSaved(_logger, exception, TokenName, SharedConfigFiles.DescribeFileReadFailure(exception));
        }

        return (refreshed, expiresAt);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The clients of CredentialsHttp are shared; SendWithRetriesAsync disposes each request.")]
    private async ValueTask<CredentialsOutcome> FetchAsync()
    {
        var (path, token, expiresAt) = LoadToken();
        var now = _dependencies.TimeProvider.GetUtcNow();
        if (_settings.SessionName is not null && expiresAt - now < TokenRefreshWindow && await TryRefreshTokenAsync(path, token).ConfigureAwait(false) is { } refreshed)
        {
            (token, expiresAt) = refreshed;
        }

        if (expiresAt <= now)
        {
            throw LoginNeeded(_settings.SessionName is null ? "has expired" : "has expired, and it could not be refreshed");
        }

        var accessToken = token.AccessToken!;
        var query = $"federation/credentials?role_name={Uri.EscapeDataString(_settings.RoleName)}&account_id={Uri.EscapeDataString(_settings.AccountId)}";
        var endpoint = new Uri(ResolveEndpoint(PortalEndpointVariable, _settings.Region.SsoPortalEndpoint), query);
        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.TryAddWithoutValidation(BearerTokenHeader, accessToken);
            return request;
        }

        var http = CredentialsHttp.GetClientFor(endpoint, _dependencies.Handler);
        var result = await CredentialsHttp.SendWithRetriesAsync(http, CreateRequest, IsTransient, _dependencies.TimeProvider).ConfigureAwait(false);
        if (result.IsOk)
        {
            return TryParseRoleCredentials(result.Body, out var credentials) is { } problem
                ? throw new CredentialsUnavailableException($"The SSO portal returned an unusable response: {problem}.")
                : CredentialsOutcome.Found(credentials!);
        }

        if (IsTransient(result))
        {
            throw new CredentialsUnavailableException(
                $"The SSO portal at {endpoint.DnsSafeHost} did not return credentials in {CredentialsHttp.MaxAttempts} attempts. The last attempt: {result.DescribeFailure()}.");
        }

        throw new CredentialsUnavailableException($"The SSO portal did not return credentials for the role {_settings.RoleName} in the account {_settings.AccountId}: {DescribeRejection(result.StatusCode)}.");
    }

    /// <summary>What a fetch needs to know.</summary>
    /// <param name="StartUrl">The start URL of the portal.</param>
    /// <param name="Region">The region of IAM Identity Center.</param>
    /// <param name="AccountId">The account of the role.</param>
    /// <param name="RoleName">The name of the role.</param>
    /// <param name="SessionName">The <c>sso-session</c>, or <see langword="null"/> for the older form.</param>
    /// <param name="SignIn">The sentence of a message that says how to sign in again, such as <c>Run `aws sso login --profile dev` to sign in again.</c>, with its period.</param>
    internal sealed record Settings(string StartUrl, AwsRegion Region, string AccountId, string RoleName, string? SessionName, string SignIn);
}
