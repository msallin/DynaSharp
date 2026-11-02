using Microsoft.Extensions.Logging;

namespace DynaSharp.Telemetry;

/// <summary>
/// Log messages of the credential providers. Only events outside a single request are logged: which source the default chain
/// chose, refreshes that failed, and credentials that the service rejected as expired. Event ids are stable. Messages never contain keys, tokens or file content. Of a URL they
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
}
