namespace DynaSharp.Credentials;

/// <summary>
/// Where a provider that calls AWS STS sends its request: the region and the endpoint of <see cref="WebIdentityCredentialsProvider"/>, and of
/// <see cref="AssumeRoleCredentialsProvider"/> through <see cref="AssumeRoleOptions"/>.
/// </summary>
/// <example>
/// A provider that trades a web identity token at a VPC endpoint of STS:
/// <code>
/// var options = new StsOptions { StsEndpoint = new Uri("https://vpce-0a1b2c3d.sts.eu-central-2.vpce.amazonaws.com") };
/// var provider = new WebIdentityCredentialsProvider("/var/run/secrets/token", "arn:aws:iam::123456789012:role/orders", options: options);
/// </code>
/// </example>
public class StsOptions
{
    /// <summary>
    /// Gets the region of the STS endpoint, which is also the region an <c>AssumeRole</c> request is signed for. <see langword="null"/>, the default, looks
    /// for it as a client does, in <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c> and the profile, at each fetch that needs it.
    /// </summary>
    public AwsRegion? Region { get; init; }

    /// <summary>
    /// Gets the endpoint of STS, for example a VPC endpoint or <c>StubSts.Endpoint</c> of <c>DynaSharp.Testing</c>: an absolute http or https URL without
    /// user info, path, query or fragment. It goes ahead of <c>AWS_ENDPOINT_URL_STS</c>, <c>AWS_ENDPOINT_URL</c>, <c>AWS_STS_REGIONAL_ENDPOINTS</c> and the
    /// region, and over http the request travels in the clear. <c>AssumeRole</c> is signed for <see cref="Region"/> or the region found as a client finds it,
    /// and for <c>us-east-1</c> when there is none. <see langword="null"/>, the default, works the endpoint out at each fetch.
    /// </summary>
    public Uri? StsEndpoint { get; init; }
}
