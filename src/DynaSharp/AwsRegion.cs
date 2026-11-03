using System.Buffers;

namespace DynaSharp;

/// <summary>
/// An AWS region, such as <c>eu-central-2</c>, and the DynamoDB and DynamoDB Streams endpoints that serve it.
/// </summary>
/// <remarks>
/// The region name ends up in the endpoint host name and in the SigV4 credential scope. It is therefore
/// validated up front: only lowercase ASCII letters, digits and inner hyphens are accepted. This rejects
/// typos early and makes it impossible to inject other hosts through the region name.
/// <para>
/// Regions in the <c>aws-cn</c> partition (names starting with <c>cn-</c>) resolve to the
/// <c>amazonaws.com.cn</c> domain. All other regions resolve to <c>amazonaws.com</c>.
/// </para>
/// </remarks>
public sealed record AwsRegion
{
    private const string ChinaPrefix = "cn-";

    private static readonly SearchValues<char> AllowedCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-");

    private AwsRegion(string name, string domain)
    {
        Name = name;
        DynamoDbEndpoint = new Uri($"https://dynamodb.{name}.{domain}/");
        DynamoDbStreamsEndpoint = new Uri($"https://streams.dynamodb.{name}.{domain}/");
        StsEndpoint = new Uri($"https://sts.{name}.{domain}/");
        SsoPortalEndpoint = new Uri($"https://portal.sso.{name}.{domain}/");
        SsoOidcEndpoint = new Uri($"https://oidc.{name}.{domain}/");
    }

    /// <summary>
    /// Gets the region name, for example <c>us-east-1</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the public HTTPS endpoint of DynamoDB in this region, for example
    /// <c>https://dynamodb.us-east-1.amazonaws.com/</c>.
    /// </summary>
    public Uri DynamoDbEndpoint { get; }

    /// <summary>
    /// Gets the public HTTPS endpoint of DynamoDB Streams in this region, for example
    /// <c>https://streams.dynamodb.us-east-1.amazonaws.com/</c>. It is a different host from <see cref="DynamoDbEndpoint"/>.
    /// </summary>
    public Uri DynamoDbStreamsEndpoint { get; }

    /// <summary>Gets the regional HTTPS endpoint of AWS STS, for example <c>https://sts.us-east-1.amazonaws.com/</c>.</summary>
    internal Uri StsEndpoint { get; }

    /// <summary>Gets the endpoint of the portal of AWS IAM Identity Center, for example <c>https://portal.sso.us-east-1.amazonaws.com/</c>.</summary>
    internal Uri SsoPortalEndpoint { get; }

    /// <summary>Gets the endpoint of the OIDC service of AWS IAM Identity Center, for example <c>https://oidc.us-east-1.amazonaws.com/</c>.</summary>
    internal Uri SsoOidcEndpoint { get; }

    /// <summary>
    /// Creates a region from its name.
    /// </summary>
    /// <param name="name">The region name, for example <c>eu-central-2</c>. It must be lowercase.</param>
    /// <returns>The region.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, contains characters other than lowercase ASCII letters, digits and
    /// hyphens, or starts or ends with a hyphen.
    /// </exception>
    public static AwsRegion FromName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (name.AsSpan().ContainsAnyExcept(AllowedCharacters) || name[0] == '-' || name[^1] == '-')
        {
            throw new ArgumentException(
                $"'{name}' is not a valid AWS region name. Expected lowercase letters, digits and inner hyphens, for example 'eu-central-2'.",
                nameof(name));
        }

        return new AwsRegion(name, name.StartsWith(ChinaPrefix, StringComparison.Ordinal) ? "amazonaws.com.cn" : "amazonaws.com");
    }

    /// <summary>
    /// Returns the region name.
    /// </summary>
    /// <returns>The value of <see cref="Name"/>.</returns>
    public override string ToString() => Name;
}
