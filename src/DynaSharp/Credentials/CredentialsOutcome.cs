namespace DynaSharp.Credentials;

/// <summary>
/// What one credentials source answered: credentials, that it does not apply, or that it applies but is broken. Only
/// <see cref="CredentialsOutcomeKind.DoesNotApply"/> lets the default chain move on, because any other source would sign as a different principal. A
/// broken source may also throw <see cref="CredentialsUnavailableException"/>, which ends the chain the same way.
/// </summary>
internal readonly record struct CredentialsOutcome
{
    private CredentialsOutcome(CredentialsOutcomeKind kind, AwsCredentials? credentials, string? reason)
    {
        Kind = kind;
        Credentials = credentials;
        Reason = reason;
    }

    /// <summary>Gets what the answer means for the default chain.</summary>
    public CredentialsOutcomeKind Kind { get; }

    /// <summary>Gets the credentials, or <see langword="null"/> if the source has none.</summary>
    public AwsCredentials? Credentials { get; }

    /// <summary>Gets why there are no credentials, for example <c>AWS_ACCESS_KEY_ID is not set</c>. Never contains secrets.</summary>
    public string? Reason { get; }

    public static CredentialsOutcome Found(AwsCredentials credentials) => new(CredentialsOutcomeKind.Found, credentials, null);

    public static CredentialsOutcome DoesNotApply(string reason) => new(CredentialsOutcomeKind.DoesNotApply, null, reason);

    public static CredentialsOutcome Broken(string reason) => new(CredentialsOutcomeKind.Broken, null, reason);

    /// <summary>Gets the credentials for a caller to which having none is an error: a provider used on its own, or the default chain at a broken source.</summary>
    /// <param name="source">Names the source in the message, for example <c>shared AWS files</c>.</param>
    /// <returns>The credentials.</returns>
    /// <exception cref="CredentialsUnavailableException">The source has none. The message gives the reason.</exception>
    public AwsCredentials GetCredentials(string source) =>
        Credentials ?? throw new CredentialsUnavailableException($"No credentials from the {source}: {Reason}.");
}
