namespace DynaSharp.Credentials;

/// <summary>
/// Supplies the credentials used to sign each request.
/// </summary>
/// <remarks>
/// <para>
/// Called for every attempt of every request, so implementations must be cheap when credentials are cached and must be thread-safe.
/// Return the same <see cref="AwsCredentials"/> instance for as long as the credentials are unchanged: the signer caches
/// its derived signing key per instance. A provider whose credentials can be replaced before they run out also implements
/// <see cref="IRefreshableCredentialsProvider"/>, so that the client can recover from credentials that DynamoDB rejects as expired.
/// </para>
/// <para>
/// An adapter over another SDK, whose credentials come as a new object per call, keeps the instance it made until they change:
/// </para>
/// <code>
/// public sealed class OtherSdkCredentialsProvider(OtherSdkCredentials source) : ICredentialsProvider
/// {
///     private volatile AwsCredentials? _current;
///
///     public async ValueTask&lt;AwsCredentials&gt; GetCredentialsAsync(CancellationToken cancellationToken)
///     {
///         var latest = await source.GetAsync(cancellationToken);
///         var current = _current;
///         if (current is null || current.AccessKeyId != latest.AccessKey || current.SecretAccessKey != latest.SecretKey || current.SessionToken != latest.Token)
///         {
///             current = new AwsCredentials(latest.AccessKey, latest.SecretKey, latest.Token);
///             _current = current;
///         }
///
///         return current;
///     }
/// }
/// </code>
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
