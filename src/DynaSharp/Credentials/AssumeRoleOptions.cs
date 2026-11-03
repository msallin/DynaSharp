namespace DynaSharp.Credentials;

/// <summary>
/// The optional settings of <see cref="AssumeRoleCredentialsProvider"/>, which a profile spells <c>external_id</c> and <c>duration_seconds</c>.
/// </summary>
public sealed class AssumeRoleOptions
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

    /// <summary>
    /// Gets the region of the STS endpoint, which is also the region the request is signed for. <see langword="null"/>, the default, looks for it
    /// as a client does, in <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c> and the profile, at each fetch that needs it.
    /// </summary>
    public AwsRegion? Region { get; init; }

    /// <summary>
    /// Gets the endpoint of STS, for example a VPC endpoint: an absolute http or https URL without user info, path, query or fragment. It goes ahead of
    /// <c>AWS_ENDPOINT_URL_STS</c>, <c>AWS_ENDPOINT_URL</c> and the region. The request is signed for <see cref="Region"/> or the region found as a
    /// client finds it, and for <c>us-east-1</c> when there is none. <see langword="null"/>, the default, works the endpoint out at each fetch.
    /// </summary>
    public Uri? StsEndpoint { get; init; }
}
