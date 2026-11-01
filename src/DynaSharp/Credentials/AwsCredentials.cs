namespace DynaSharp.Credentials;

/// <summary>
/// AWS credentials used to sign requests: an access key pair and, for temporary credentials, a session token.
/// </summary>
/// <remarks>
/// <see cref="ToString"/> never includes the secret access key or the session token, so instances are safe to log.
/// Instances are immutable; a provider that refreshes credentials returns a new instance.
/// </remarks>
public sealed class AwsCredentials
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AwsCredentials"/> class.
    /// </summary>
    /// <param name="accessKeyId">The access key id, for example <c>AKIAIOSFODNN7EXAMPLE</c>.</param>
    /// <param name="secretAccessKey">The secret access key.</param>
    /// <param name="sessionToken">The session token of temporary credentials, or <see langword="null"/> for long-term keys.</param>
    /// <param name="expiration">When temporary credentials expire, or <see langword="null"/> if they do not.</param>
    /// <exception cref="ArgumentException"><paramref name="accessKeyId"/> or <paramref name="secretAccessKey"/> is null or empty.</exception>
    public AwsCredentials(string accessKeyId, string secretAccessKey, string? sessionToken = null, DateTimeOffset? expiration = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessKeyId);
        ArgumentException.ThrowIfNullOrEmpty(secretAccessKey);

        AccessKeyId = accessKeyId;
        SecretAccessKey = secretAccessKey;
        SessionToken = string.IsNullOrEmpty(sessionToken) ? null : sessionToken;
        Expiration = expiration;
    }

    /// <summary>Gets the access key id.</summary>
    public string AccessKeyId { get; }

    /// <summary>Gets the secret access key.</summary>
    public string SecretAccessKey { get; }

    /// <summary>Gets the session token, or <see langword="null"/> for long-term keys. An empty token is normalized to <see langword="null"/>.</summary>
    public string? SessionToken { get; }

    /// <summary>Gets when the credentials expire, or <see langword="null"/> if they do not.</summary>
    public DateTimeOffset? Expiration { get; }

    /// <summary>Returns a description with the access key id only; secrets are never included.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => $"AwsCredentials {{ AccessKeyId = {AccessKeyId}, Expiration = {Expiration?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "never"} }}";
}
