using DynaSharp.Credentials;
using DynaSharp.Signing;

namespace DynaSharp.UnitTests.Signing;

/// <summary>Lets the signer tests hand the signer a request, as the vectors describe it, instead of its parts.</summary>
internal static class SigV4SignerExtensions
{
    /// <summary>Signs a request with the authority of its URI and the target it carries, which is what the pipeline passes.</summary>
    public static void Sign(this SigV4Signer signer, HttpRequestMessage request, ReadOnlySpan<byte> payloadSha256, AwsCredentials credentials, DateTimeOffset utcNow) =>
        signer.Sign(request, SigV4Signer.GetAuthority(request.RequestUri!), request.Headers.NonValidated["X-Amz-Target"].ToString(), payloadSha256, credentials, utcNow);
}
