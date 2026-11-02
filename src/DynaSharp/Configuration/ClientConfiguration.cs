using DynaSharp.Credentials;
using DynaSharp.Retries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.Configuration;

/// <summary>
/// The fully resolved settings of a client: <see cref="DynamoDbClientOptions"/> merged with the environment, validated,
/// and with every default applied. Created once per client by <see cref="Resolve"/>.
/// </summary>
internal sealed record ClientConfiguration
{
    private const string OptionsSource = "options";
    private const string ProfileSource = "profile";
    private const string RegionSourceDefault = "region";
    private const string RegionVariable = "AWS_REGION";
    private const string DefaultRegionVariable = "AWS_DEFAULT_REGION";
    private const string DynamoDbEndpointVariable = "AWS_ENDPOINT_URL_DYNAMODB";
    private const string DynamoDbStreamsEndpointVariable = "AWS_ENDPOINT_URL_DYNAMODB_STREAMS";
    private const string EndpointVariable = "AWS_ENDPOINT_URL";

    // The largest delay CancellationTokenSource.CancelAfter and TimeProvider.CreateCancellationTokenSource accept: about 49.7 days.
    private static readonly TimeSpan MaxAttemptTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>Gets the region requests are signed for.</summary>
    public required AwsRegion Region { get; init; }

    /// <summary>
    /// Gets where <see cref="Region"/> came from: <c>options</c>, <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c> or <c>profile</c>
    /// (the region setting of the selected profile in the shared AWS files). For diagnostics.
    /// </summary>
    public required string RegionSource { get; init; }

    /// <summary>Gets the endpoint requests are sent to. Always absolute, http or https, without user info, path, query or fragment.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>
    /// Gets where <see cref="Endpoint"/> came from: <c>options</c>, <c>AWS_ENDPOINT_URL_DYNAMODB</c> (for DynamoDB Streams
    /// <c>AWS_ENDPOINT_URL_DYNAMODB_STREAMS</c>), <c>AWS_ENDPOINT_URL</c> or <c>region</c> (the public endpoint of <see cref="Region"/>).
    /// For diagnostics.
    /// </summary>
    public required string EndpointSource { get; init; }

    /// <summary>
    /// Gets the endpoint if someone chose it, in the options or the environment, or <see langword="null"/> for the public endpoint of <see cref="Region"/>. That one follows from the region and the service, so a copy
    /// of the options for another region or the other client must not carry it along.
    /// </summary>
    public Uri? ChosenEndpoint => EndpointSource == RegionSourceDefault ? null : Endpoint;

    /// <summary>Gets the credentials provider.</summary>
    public required ICredentialsProvider Credentials { get; init; }

    /// <summary>Gets the timeout of a single HTTP attempt: positive, or <see cref="Timeout.InfiniteTimeSpan"/> for none.</summary>
    public required TimeSpan AttemptTimeout { get; init; }

    /// <summary>Gets the maximum number of attempts per operation including the first: at least 1, where 1 means no retries.</summary>
    public required int MaxAttempts { get; init; }

    /// <summary>Gets the local clock, for timeouts and retry delays.</summary>
    public required TimeProvider TimeProvider { get; init; }

    /// <summary>
    /// Gets the clock of the service as far as the client knows it, for signatures and for the expiry of the credentials of the providers
    /// the client creates. The pipeline corrects it. There is one per client, so that the two cannot disagree.
    /// </summary>
    public required ClockSkewCorrection Clock { get; init; }

    /// <summary>Gets the logger factory. Logs nothing by default.</summary>
    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    /// <summary>
    /// Merges the options with the environment and validates the result.
    /// </summary>
    /// <param name="options">
    /// The options. Explicit values win over the environment: region, endpoint and credentials each fall back to
    /// the environment (and then to a default) only when the option is unset.
    /// </param>
    /// <param name="getEnvironmentVariable">Reads an environment variable. An unset and an empty variable count the same.</param>
    /// <param name="readFile">
    /// Reads the shared AWS files, for the region of the selected profile and for the default credentials. <see langword="null"/>
    /// reads from disk. The files are only read when the region is not set elsewhere, and credentials are not looked up at all.
    /// </param>
    /// <param name="service">The service the client talks to. It selects the default endpoint and the endpoint environment variable.</param>
    /// <param name="credentialsHandler">Answers for the credential endpoints of the default chain, or <see langword="null"/> to call them.</param>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or its <see cref="DynamoDbClientOptions.TimeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="DynamoDbClientOptions.AttemptTimeout"/> is neither infinite nor between zero (exclusive) and 4294967294 milliseconds (about 49.7 days),
    /// or <see cref="DynamoDbClientOptions.MaxAttempts"/> is less than 1.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="DynamoDbClientOptions.ServiceUrl"/> is not an absolute http or https URL without user info, path, query and fragment.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No region is set (the message says what was tried, including why the shared files did not help), or a region or endpoint
    /// environment variable or the region of the profile has an invalid value. The message names the variable or the profile.
    /// </exception>
    public static ClientConfiguration Resolve(DynamoDbClientOptions options, Func<string, string?> getEnvironmentVariable, ReadTextFile? readFile = null, AwsService service = AwsService.DynamoDb, HttpMessageHandler? credentialsHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);

        var attemptTimeout = options.AttemptTimeout;
        if (attemptTimeout != Timeout.InfiniteTimeSpan && (attemptTimeout <= TimeSpan.Zero || attemptTimeout > MaxAttemptTimeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                attemptTimeout,
                $"{nameof(DynamoDbClientOptions.AttemptTimeout)} must be Timeout.InfiniteTimeSpan or positive and at most {MaxAttemptTimeout.TotalMilliseconds} milliseconds (about 49.7 days).");
        }

        if (options.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxAttempts,
                $"{nameof(DynamoDbClientOptions.MaxAttempts)} must be at least 1, which sends every request once and does not retry.");
        }

        readFile ??= SharedConfigFiles.DiskReader;
        var loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        var (region, regionSource) = ResolveRegion(options, getEnvironmentVariable, readFile);
        var (endpoint, endpointSource) = ResolveEndpoint(options, region, getEnvironmentVariable, service);
        var clock = new ClockSkewCorrection(options.TimeProvider);

        return new ClientConfiguration
        {
            Region = region,
            RegionSource = regionSource,
            Endpoint = endpoint,
            EndpointSource = endpointSource,
            Credentials = options.Credentials
                ?? new DefaultCredentialsProvider(getEnvironmentVariable, readFile, credentialsHandler, clock, loggerFactory, null, region),
            AttemptTimeout = attemptTimeout,
            MaxAttempts = options.MaxAttempts,
            TimeProvider = options.TimeProvider,
            Clock = clock,
            LoggerFactory = loggerFactory,
        };
    }

    /// <summary>Finds the region: the options, then <c>AWS_REGION</c>, <c>AWS_DEFAULT_REGION</c> and the selected profile.</summary>
    /// <param name="options">The options.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="readFile">Reads the shared AWS files.</param>
    /// <returns>The region and where it came from.</returns>
    /// <exception cref="InvalidOperationException">No region is set, or the one that is set is invalid. The message says what was tried.</exception>
    internal static (AwsRegion Region, string Source) ResolveRegion(DynamoDbClientOptions options, Func<string, string?> getEnvironmentVariable, ReadTextFile readFile)
    {
        if (options.Region is not null)
        {
            return (options.Region, OptionsSource);
        }

        var (name, source) = ReadFirstSet(getEnvironmentVariable, RegionVariable, DefaultRegionVariable);
        string? profileName = null;
        if (string.IsNullOrEmpty(name))
        {
            var shared = SharedConfigFiles.FindRegion(getEnvironmentVariable, readFile);
            if (shared.Name is null)
            {
                throw new InvalidOperationException(
                    $"No AWS region configured. Set {nameof(DynamoDbClientOptions)}.{nameof(DynamoDbClientOptions.Region)}, " +
                    $"the {RegionVariable} (or {DefaultRegionVariable}) environment variable, or 'region' in the profile '{shared.ProfileName}' " +
                    $"of the shared AWS config file. That profile did not help: {shared.Problem}.");
            }

            (name, source, profileName) = (shared.Name, ProfileSource, shared.ProfileName);
        }

        try
        {
            return (AwsRegion.FromName(name), source);
        }
        catch (ArgumentException exception)
        {
            var origin = profileName is null ? $"The {source} environment variable" : $"The region setting of the profile '{profileName}'";
            throw new InvalidOperationException($"{origin} contains '{name}', which is not a valid AWS region name.", exception);
        }
    }

    /// <summary>Finds the endpoint that the environment chooses for a service: the variable of the service, then <c>AWS_ENDPOINT_URL</c>, which is botocore's order. An empty variable counts as unset.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="serviceVariable">The variable of the service, such as <c>AWS_ENDPOINT_URL_STS</c>.</param>
    /// <returns>The endpoint, or <see langword="null"/> if neither is set, and the variable it came from.</returns>
    /// <exception cref="InvalidOperationException">The variable that is set is no valid endpoint. The message names the variable, and not its value.</exception>
    internal static (Uri? Endpoint, string Source) FindEndpointInEnvironment(Func<string, string?> getEnvironmentVariable, string serviceVariable)
    {
        var (value, source) = ReadFirstSet(getEnvironmentVariable, serviceVariable, EndpointVariable);
        if (string.IsNullOrEmpty(value))
        {
            return (null, source);
        }

        // The value is not echoed: an endpoint may carry credentials in its user info.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"The {source} environment variable is not a valid endpoint: it is not an absolute URL.");
        }

        if (FindEndpointProblem(endpoint) is { } problem)
        {
            throw new InvalidOperationException($"The {source} environment variable is not a valid endpoint: {problem}.");
        }

        return (endpoint, source);
    }

    private static (Uri Endpoint, string Source) ResolveEndpoint(DynamoDbClientOptions options, AwsRegion region, Func<string, string?> getEnvironmentVariable, AwsService service)
    {
        if (options.ServiceUrl is { } serviceUrl)
        {
            if (FindEndpointProblem(serviceUrl) is { } problem)
            {
                throw new ArgumentException($"{nameof(DynamoDbClientOptions.ServiceUrl)} is not a valid endpoint: {problem}.", nameof(options));
            }

            return (serviceUrl, OptionsSource);
        }

        var streams = service == AwsService.DynamoDbStreams;
        var (endpoint, source) = FindEndpointInEnvironment(getEnvironmentVariable, streams ? DynamoDbStreamsEndpointVariable : DynamoDbEndpointVariable);
        return endpoint is null ? (streams ? region.DynamoDbStreamsEndpoint : region.DynamoDbEndpoint, RegionSourceDefault) : (endpoint, source);
    }

    // An unset and an empty variable count the same, as in the AWS SDKs.
    private static (string? Value, string Source) ReadFirstSet(Func<string, string?> getEnvironmentVariable, string first, string second)
    {
        var value = getEnvironmentVariable(first);
        return string.IsNullOrEmpty(value) ? (getEnvironmentVariable(second), second) : (value, first);
    }

    private static string? FindEndpointProblem(Uri endpoint) => endpoint switch
    {
        { IsAbsoluteUri: false } => "it is not an absolute URL",
        { Scheme: not ("http" or "https") } => "the scheme must be http or https",
        { UserInfo.Length: > 0 } => "it must not have user info",
        { AbsolutePath: not "/" } => "it must not have a path",
        { Query.Length: > 0 } => "it must not have a query",
        { Fragment.Length: > 0 } => "it must not have a fragment",
        _ => null,
    };
}
