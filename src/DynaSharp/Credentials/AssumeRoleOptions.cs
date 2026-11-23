namespace DynaSharp.Credentials;

/// <summary>
/// The optional settings of <see cref="AssumeRoleCredentialsProvider"/>, which a profile spells <c>external_id</c> and <c>duration_seconds</c>, besides the
/// region and the endpoint of STS.
/// </summary>
public sealed class AssumeRoleOptions : StsOptions
{
    /// <summary>
    /// Gets the external id that the trust policy of the role asks for, which a third party that assumes a role in the account of a customer
    /// passes (the confused deputy problem). <see langword="null"/>, the default, sends none.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// Gets how long the session lasts: 15 minutes to 12 hours in whole seconds, and at most the maximum session duration of the role, which STS
    /// checks. <see langword="null"/>, the default, asks for none, so the session lasts the default hour. A session of 15 minutes is refreshed
    /// every 30 seconds, because it never leaves the advisory refresh period of 15 minutes before its expiration.
    /// </summary>
    public TimeSpan? Duration { get; init; }
}
