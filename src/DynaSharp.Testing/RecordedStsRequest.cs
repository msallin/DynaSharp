using System.Net;
using DynaSharp.Credentials;

namespace DynaSharp.Testing;

/// <summary>
/// A request that a <see cref="StubSts"/> received, read from the socket and decoded the way botocore encodes a form: RFC 3986 percent encoding, and a space as <c>+</c>.
/// </summary>
/// <param name="Action">The <c>Action</c> of the form: <c>AssumeRoleWithWebIdentity</c> for what <see cref="WebIdentityCredentialsProvider"/> sends, <c>AssumeRole</c> for <see cref="AssumeRoleCredentialsProvider"/>. A field the form lacks is empty, here and in the next three.</param>
/// <param name="RoleArn">The <c>RoleArn</c> of the form.</param>
/// <param name="RoleSessionName">The <c>RoleSessionName</c> of the form.</param>
/// <param name="WebIdentityToken">The <c>WebIdentityToken</c> of the form, which <c>AssumeRole</c> does not have.</param>
/// <param name="Form">Every field of the form by name, decoded, such as <c>Version</c>.</param>
/// <param name="Headers">The lines of the headers as they came, such as <c>Content-Type: application/x-www-form-urlencoded; charset=utf-8</c>.</param>
public sealed record RecordedStsRequest(string Action, string RoleArn, string RoleSessionName, string WebIdentityToken, IReadOnlyDictionary<string, string> Form, IReadOnlyList<string> Headers)
{
    /// <summary>Gets the <c>ExternalId</c> of <see cref="Form"/>, which <c>AssumeRole</c> has for <see cref="AssumeRoleOptions.ExternalId"/>, or empty if the form lacks it.</summary>
    public string ExternalId => Form.GetValueOrDefault("ExternalId", string.Empty);

    /// <summary>
    /// Gets the <c>DurationSeconds</c> of <see cref="Form"/> as sent, such as <c>3600</c>, which <c>AssumeRole</c> has for <see cref="AssumeRoleOptions.Duration"/>, or empty if the form lacks it, as it does when STS picks
    /// the duration.
    /// </summary>
    public string DurationSeconds => Form.GetValueOrDefault("DurationSeconds", string.Empty);

    internal static RecordedStsRequest Create(ReceivedRequest request)
    {
        var form = ReadForm(request.Body);
        return new RecordedStsRequest(
            form.GetValueOrDefault("Action", string.Empty),
            form.GetValueOrDefault("RoleArn", string.Empty),
            form.GetValueOrDefault("RoleSessionName", string.Empty),
            form.GetValueOrDefault("WebIdentityToken", string.Empty),
            form,
            request.Headers);
    }

    // Input:  "Action=AssumeRoleWithWebIdentity&RoleArn=arn%3Aaws%3Aiam%3A%3A1%3Arole%2Fa&WebIdentityToken=a%2Bb+c"
    // Result: Action = "AssumeRoleWithWebIdentity", RoleArn = "arn:aws:iam::1:role/a", WebIdentityToken = "a+b c"
    private static Dictionary<string, string> ReadForm(string body)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = field.Split('=', 2);
            form[WebUtility.UrlDecode(parts[0])] = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
        }

        return form;
    }
}
