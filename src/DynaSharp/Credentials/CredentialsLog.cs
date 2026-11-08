using Microsoft.Extensions.Logging;

namespace DynaSharp.Credentials;

/// <summary>
/// Log messages of the credential providers. Only events outside a single request are logged: which source the default chain and a profile
/// chose, refreshes that failed, credentials that the service rejected as expired, refreshes of the cached SSO token, and an STS endpoint over plain http. Event ids are stable. Messages never contain keys, tokens or file content. Of a URL they
/// show at most the host, which the reason of a failed refresh names for the container endpoint: the host is no secret, and the rest of that URI holds a credential identifier.
/// </summary>
internal static partial class CredentialsLog
{
    [LoggerMessage(EventId = 20, Level = LogLevel.Debug, Message = "Credentials come from the {Source}.")]
    public static partial void SourceChosen(ILogger logger, string source);

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Warning,
        Message = "Refreshing the credentials from the {Source} failed in the advisory refresh period: {Reason}. The current credentials stay in use.")]
    public static partial void AdvisoryRefreshFailed(ILogger logger, Exception? exception, string source, string reason);

    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Warning,
        Message = "Refreshing the credentials from the {Source} failed in the mandatory refresh period: {Reason}. Requests fail until a refresh succeeds.")]
    public static partial void MandatoryRefreshFailed(ILogger logger, Exception? exception, string source, string reason);

    [LoggerMessage(
        EventId = 23,
        Level = LogLevel.Warning,
        Message = "DynamoDB rejected the credentials from the {Source} as expired. They are dropped and fetched again.")]
    public static partial void ExpiredCredentialsDropped(ILogger logger, string source);

    [LoggerMessage(
        EventId = 24,
        Level = LogLevel.Warning,
        Message = "STS rejected the token of the {Source} as expired. The file is read again and the request is sent once more.")]
    public static partial void WebIdentityTokenExpired(ILogger logger, string source);

    [LoggerMessage(
        EventId = 25,
        Level = LogLevel.Warning,
        Message = "STS is reached at {Host} over plain http. The web identity token in the request and the session token in the response travel unencrypted.")]
    public static partial void UnencryptedStsEndpoint(ILogger logger, string host);

    [LoggerMessage(EventId = 26, Level = LogLevel.Debug, Message = "The profile '{Profile}' gets its credentials from {Source}.")]
    public static partial void ProfileSourceChosen(ILogger logger, string profile, string source);

    [LoggerMessage(
        EventId = 27,
        Level = LogLevel.Warning,
        Message = "Refreshing the cached SSO token of {Token} failed: {Reason}. The cached token is used while it lasts.")]
    public static partial void SsoTokenRefreshFailed(ILogger logger, string token, string reason);

    [LoggerMessage(
        EventId = 28,
        Level = LogLevel.Warning,
        Message = "The refreshed SSO token of {Token} could not be written to the token cache: {Reason}. It is used all the same, and refreshed again at the next fetch.")]
    public static partial void SsoTokenNotSaved(ILogger logger, Exception exception, string token, string reason);

    [LoggerMessage(EventId = 29, Level = LogLevel.Debug, Message = "The cached SSO token of {Token} was refreshed.")]
    public static partial void SsoTokenRefreshed(ILogger logger, string token);

    [LoggerMessage(
        EventId = 30,
        Level = LogLevel.Warning,
        Message = "The local clock differs from the STS clock by {Offset}. The signing time is corrected and the request is sent again.")]
    public static partial void ClockSkewCorrected(ILogger logger, TimeSpan offset);
}
