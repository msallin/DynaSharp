using System.Buffers;
using System.Globalization;
using DynaSharp.Configuration;

namespace DynaSharp.Credentials;

/// <summary>
/// What the two callers of AWS STS share, <see cref="WebIdentityCredentialsProvider"/> and <see cref="AssumeRoleCredentialsProvider"/>: the
/// endpoint, the rule of a session name and the encoding of the form.
/// </summary>
internal static class Sts
{
    /// <summary>The rule of a session name, for messages.</summary>
    public const string SessionNameRule = "must be 2 to 64 letters, digits or characters of _+=,.@-";

    /// <summary>The variable of the endpoint of STS, which goes ahead of <c>AWS_ENDPOINT_URL</c>.</summary>
    public const string EndpointVariable = "AWS_ENDPOINT_URL_STS";

    private const string RegionalEndpointsVariable = "AWS_STS_REGIONAL_ENDPOINTS";
    private const int MinSessionNameLength = 2;
    private const int MaxSessionNameLength = 64;

    // botocore's LEGACY_GLOBAL_STS_REGIONS, in args.py.
    private static readonly string[] LegacyGlobalRegions =
    [
        "ap-northeast-1", "ap-south-1", "ap-southeast-1", "ap-southeast-2", "ca-central-1", "eu-central-1", "eu-north-1", "eu-west-1",
        "eu-west-2", "eu-west-3", "sa-east-1", "us-east-1", "us-east-2", "us-west-1", "us-west-2",
    ];

    // The pattern of roleSessionNameType is [\w+=,.@-]*, where \w means ASCII; a .NET \w would let other letters through.
    private static readonly SearchValues<char> SessionNameCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_+=,.@-");

    /// <summary>Gets the global endpoint, which botocore's legacy mode keeps for the regions that used it before regional endpoints existed. It signs for <c>us-east-1</c>.</summary>
    public static Uri GlobalEndpoint { get; } = new("https://sts.amazonaws.com/");

    /// <summary>Tells whether a name is a valid session name.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> if it follows <see cref="SessionNameRule"/>.</returns>
    public static bool IsValidSessionName(string name) =>
        name.Length is >= MinSessionNameLength and <= MaxSessionNameLength && !name.AsSpan().ContainsAnyExcept(SessionNameCharacters);

    /// <summary>The session name of a fetch that was given none: <c>dynasharp-</c> and the Unix time in seconds, where botocore says <c>botocore-session-</c>.</summary>
    /// <param name="timeProvider">The clock.</param>
    /// <returns>The name.</returns>
    public static string DefaultSessionName(TimeProvider timeProvider) =>
        string.Create(CultureInfo.InvariantCulture, $"dynasharp-{timeProvider.GetUtcNow().ToUnixTimeSeconds()}");

    /// <summary>Encodes a value of the form as botocore's <c>urlencode</c> does, so the body is the same byte for byte: RFC 3986 percent encoding, and a space as <c>+</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The encoded value.</returns>
    // Input: "arn:aws:iam::1:role/a b"   Result: "arn%3Aaws%3Aiam%3A%3A1%3Arole%2Fa+b"
    public static string Encode(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);

    /// <summary>
    /// Finds the endpoint as botocore does: <paramref name="explicitEndpoint"/>, then <c>AWS_ENDPOINT_URL_STS</c> and <c>AWS_ENDPOINT_URL</c>, then the
    /// regional endpoint, or the global one for <c>AWS_STS_REGIONAL_ENDPOINTS=legacy</c> (or <c>sts_regional_endpoints = legacy</c> in the selected profile) and a region of botocore's list.
    /// </summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="readFile">Reads the shared AWS files.</param>
    /// <param name="explicitEndpoint">An endpoint given in code, or <see langword="null"/>.</param>
    /// <param name="region">The region, or a function that finds it. It is asked only when no endpoint is configured.</param>
    /// <returns>
    /// The endpoint, the region it is in when it came from the region, otherwise <see langword="null"/>, and what a failed call adds to its message: <see cref="ClientConfiguration.GenericEndpointNote"/>.
    /// </returns>
    /// <exception cref="CredentialsUnavailableException">An endpoint variable is no valid endpoint, or the region is needed and not known.</exception>
    public static (Uri Endpoint, AwsRegion? Region, string FailureNote) ResolveEndpoint(Func<string, string?> getEnvironmentVariable, ReadTextFile readFile, Uri? explicitEndpoint, Func<AwsRegion> region)
    {
        if (explicitEndpoint is not null)
        {
            return (explicitEndpoint, null, string.Empty);
        }

        if (ReadConfiguredEndpoint(getEnvironmentVariable) is ({ } configured, var source))
        {
            return (configured, null, ClientConfiguration.GenericEndpointNote(source, "STS"));
        }

        var resolved = region();
        var endpoint = string.Equals(SharedConfigFiles.ReadSetting(getEnvironmentVariable, readFile, RegionalEndpointsVariable, "sts_regional_endpoints"), "legacy", StringComparison.OrdinalIgnoreCase) && Array.IndexOf(LegacyGlobalRegions, resolved.Name) >= 0
            ? GlobalEndpoint
            : resolved.StsEndpoint;
        return (endpoint, resolved, string.Empty);
    }

    /// <summary>Finds the region of a provider that no client made, as a client would: <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c>, the selected profile.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="readFile">Reads the shared AWS files.</param>
    /// <returns>The region.</returns>
    /// <exception cref="CredentialsUnavailableException">There is none: without a region there is no regional endpoint, and no reason to pick another one silently.</exception>
    public static AwsRegion ResolveRegion(Func<string, string?> getEnvironmentVariable, ReadTextFile readFile)
    {
        try
        {
            return ClientConfiguration.ResolveRegion(new DynamoDbClientOptions(), getEnvironmentVariable, readFile).Region;
        }
        catch (InvalidOperationException exception)
        {
            throw new CredentialsUnavailableException($"The STS endpoint depends on the region, and it is not known: {exception.Message}", exception);
        }
    }

    private static (Uri? Endpoint, string Source) ReadConfiguredEndpoint(Func<string, string?> getEnvironmentVariable)
    {
        try
        {
            return ClientConfiguration.FindEndpointInEnvironment(getEnvironmentVariable, EndpointVariable);
        }
        catch (InvalidOperationException exception)
        {
            throw new CredentialsUnavailableException(exception.Message, exception);
        }
    }
}
