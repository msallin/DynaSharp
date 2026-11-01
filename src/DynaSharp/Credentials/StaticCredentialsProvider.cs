namespace DynaSharp.Credentials;

/// <summary>
/// Always supplies the same credentials. Use for long-term keys, tests and DynamoDB Local.
/// </summary>
/// <param name="credentials">The credentials to supply.</param>
public sealed class StaticCredentialsProvider(AwsCredentials credentials) : ICredentialsProvider
{
    private readonly AwsCredentials _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticCredentialsProvider"/> class from the parts of the credentials, for the
    /// common case of a key pair that is not temporary or whose token comes from a configuration source.
    /// </summary>
    /// <param name="accessKeyId">The access key id, for example <c>AKIAIOSFODNN7EXAMPLE</c>.</param>
    /// <param name="secretAccessKey">The secret access key.</param>
    /// <param name="sessionToken">
    /// The session token of temporary credentials, or <see langword="null"/> for long-term keys. This provider never refreshes: temporary
    /// credentials stop working when they expire.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="accessKeyId"/> or <paramref name="secretAccessKey"/> is null or empty.</exception>
    public StaticCredentialsProvider(string accessKeyId, string secretAccessKey, string? sessionToken = null)
        : this(new AwsCredentials(accessKeyId, secretAccessKey, sessionToken))
    {
    }

    /// <inheritdoc/>
    public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_credentials);
}
