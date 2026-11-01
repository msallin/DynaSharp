namespace DynaSharp.Credentials;

/// <summary>
/// A credentials provider that can let go of credentials the service has rejected, so that the next
/// <see cref="ICredentialsProvider.GetCredentialsAsync"/> gets other ones. An optional capability: a provider that does not implement it
/// keeps working, and the client passes the rejection on to the caller.
/// </summary>
/// <remarks>
/// <para>
/// DynamoDB can consider credentials expired while <see cref="AwsCredentials.Expiration"/> says that they are valid, for example when the
/// local clock is behind, when a session was revoked, or when the source renews them late. If a response says so
/// (<c>ExpiredTokenException</c>), a client calls <see cref="TryInvalidate"/> with the credentials of that request. If it returns
/// <see langword="true"/>, the client sends the request once more, looking up the credentials again, so a failing lookup surfaces as
/// its own exception. See <see cref="DynamoDbClientOptions.MaxAttempts"/> for how that resend counts.
/// </para>
/// <para>
/// Implementations must be thread-safe and must not do any input or output here: the fetch belongs in
/// <see cref="ICredentialsProvider.GetCredentialsAsync"/>, where the concurrent requests can share it and where the caller's cancellation and the attempt timeout apply.
/// Many requests in flight fail with the same credentials, one after the other, so compare <c>rejected</c> with what is held by
/// reference and do nothing if it differs: those credentials were already replaced, and dropping the newer ones would fetch again
/// for nothing.
/// </para>
/// </remarks>
public interface IRefreshableCredentialsProvider : ICredentialsProvider
{
    /// <summary>
    /// Drops the given credentials if the provider still holds them, so that the next lookup does not return them.
    /// </summary>
    /// <param name="rejected">The instance the rejected request was signed with, which <see cref="ICredentialsProvider.GetCredentialsAsync"/> returned.</param>
    /// <returns>
    /// <see langword="true"/> if the next lookup can return other credentials, which includes that it already does.
    /// <see langword="false"/> if this provider has nothing else to offer, so that sending the request again is pointless.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rejected"/> is <see langword="null"/>.</exception>
    public bool TryInvalidate(AwsCredentials rejected);
}
