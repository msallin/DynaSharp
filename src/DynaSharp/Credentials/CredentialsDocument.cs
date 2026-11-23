using System.Text.Json;

namespace DynaSharp.Credentials;

/// <summary>
/// The JSON document that the container credentials endpoint and the instance metadata service return, reduced to the fields
/// that matter. Parsed with <see cref="Utf8JsonReader"/>, without reflection.
/// </summary>
/// <remarks>
/// The container endpoint (ECS task roles, EKS Pod Identity):
/// <code>
/// {"AccessKeyId":"ASIA...","SecretAccessKey":"...","Token":"...","Expiration":"2026-10-31T18:34:56Z","RoleArn":"arn:aws:iam::123456789012:role/r"}
/// </code>
/// The instance metadata service (<c>.../security-credentials/role-name</c>):
/// <code>
/// {"Code":"Success","LastUpdated":"2012-04-26T16:39:16Z","Type":"AWS-HMAC","AccessKeyId":"ASIA...","SecretAccessKey":"...","Token":"...","Expiration":"2017-05-17T15:09:54Z"}
/// </code>
/// When the instance role cannot be assumed, the service answers with HTTP 200 and <c>{"Code":"AssumeRoleUnauthorizedAccess","Message":"..."}</c>.
/// </remarks>
internal readonly record struct CredentialsDocument(string? AccessKeyId, string? SecretAccessKey, string? Token, string? Expiration, string? Code)
{
    /// <summary>Parses a response body.</summary>
    /// <param name="json">The UTF-8 body.</param>
    /// <param name="document">The fields; properties of another type than string count as missing.</param>
    /// <returns><see langword="false"/> if the body is not a JSON object.</returns>
    public static bool TryParse(ReadOnlySpan<byte> json, out CredentialsDocument document)
    {
        if (CredentialsJson.TryRead(json) is not { } members)
        {
            document = default;
            return false;
        }

        document = new CredentialsDocument(
            members.GetString("AccessKeyId"), members.GetString("SecretAccessKey"), members.GetString("Token"), members.GetString("Expiration"), members.GetString("Code"));
        return true;
    }

    /// <summary>Builds credentials from the document.</summary>
    /// <param name="credentials">The credentials, when the method returns <see langword="null"/>.</param>
    /// <param name="tokenField">What the source calls the session token, for the message about a missing field.</param>
    /// <returns>
    /// <see langword="null"/> on success, otherwise what is wrong: the names of the missing fields, or that the expiration is not a
    /// timestamp. Never contains values.
    /// </returns>
    public string? TryCreateCredentials(out AwsCredentials? credentials, string tokenField = "Token")
    {
        credentials = null;
        var missing = CredentialsJson.DescribeMissing(
            ("AccessKeyId", string.IsNullOrEmpty(AccessKeyId)),
            ("SecretAccessKey", string.IsNullOrEmpty(SecretAccessKey)),
            (tokenField, string.IsNullOrEmpty(Token)),
            ("Expiration", string.IsNullOrEmpty(Expiration)));
        if (missing is not null)
        {
            return "the response does not contain " + missing;
        }

        if (CredentialsJson.ParseTime(Expiration) is not { } expiration)
        {
            return "the Expiration of the response is not a valid timestamp";
        }

        credentials = new AwsCredentials(AccessKeyId!, SecretAccessKey!, Token, expiration);
        return null;
    }

    /// <summary>Returns the name of the type: the generated text of a record would print the secret and the session token.</summary>
    /// <returns>The name of the type.</returns>
    public override string ToString() => nameof(CredentialsDocument);
}
