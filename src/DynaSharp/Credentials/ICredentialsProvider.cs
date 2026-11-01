namespace DynaSharp.Credentials;

/// <summary>
/// Supplies the credentials used to sign each request.
/// </summary>
/// <remarks>
/// Called for every attempt of every request, so implementations must be cheap when credentials are cached and must be thread-safe.
/// Return the same <see cref="AwsCredentials"/> instance for as long as the credentials are unchanged: the signer caches
/// its derived signing key per instance. A provider whose credentials can be replaced before they run out also implements
/// <see cref="IRefreshableCredentialsProvider"/>, so that the client can recover from credentials that DynamoDB rejects as expired.
/// </remarks>
public interface ICredentialsProvider
{
    /// <summary>
    /// Gets the current credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancels a pending credential lookup.</param>
    /// <returns>The credentials.</returns>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    public ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken);
}
