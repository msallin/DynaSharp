using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using DynaSharp.Credentials;

namespace DynaSharp.UnitTests.Signing;

/// <summary>
/// A request, the credentials and time to sign it with, and the headers botocore produced for it.
/// </summary>
internal sealed record SigV4Vector(
    string Name,
    string Region,
    string Url,
    string Target,
    string Body,
    string AccessKeyId,
    string SecretAccessKey,
    string? SessionToken,
    DateTimeOffset Time,
    string ExpectedAmzDate,
    string ExpectedAuthorization)
{
    /// <summary>Gets the SHA-256 hash of the body, which the caller of the signer computes.</summary>
    public byte[] PayloadSha256 => SHA256.HashData(Encoding.UTF8.GetBytes(Body));

    /// <summary>Creates a request the way the pipeline does: POST with the JSON content type and the target header.</summary>
    /// <returns>The unsigned request.</returns>
    public HttpRequestMessage CreateRequest()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(Body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.0");
        var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Amz-Target", Target);
        return request;
    }

    /// <summary>Creates new credentials from the keys of the vector. Every call returns a distinct instance.</summary>
    /// <returns>The credentials.</returns>
    public AwsCredentials CreateCredentials() => new(AccessKeyId, SecretAccessKey, SessionToken);
}
