using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DynaSharp.Credentials;

/// <summary>
/// A token of AWS IAM Identity Center, as <c>aws sso login</c> caches it in <c>~/.aws/sso/cache/{sha1}.json</c> and botocore reads and writes it.
/// </summary>
/// <remarks>
/// The file of a login with an <c>sso-session</c>:
/// <code>
/// {"startUrl": "https://my-sso-portal.awsapps.com/start", "region": "us-east-1", "accessToken": "...", "expiresAt": "2026-11-03T20:00:00Z",
///  "clientId": "...", "clientSecret": "...", "registrationExpiresAt": "2027-01-31T08:00:00Z", "refreshToken": "..."}
/// </code>
/// A login of the older form without a session has only the first four. Members of another type than string count as missing. Every member but
/// <c>startUrl</c> and <c>region</c> is a secret, and <see cref="ToString"/> shows none.
/// </remarks>
/// <param name="StartUrl">The start URL of the portal.</param>
/// <param name="Region">The region of IAM Identity Center.</param>
/// <param name="AccessToken">The bearer token of the portal.</param>
/// <param name="ExpiresAt">When <paramref name="AccessToken"/> expires, as written.</param>
/// <param name="ClientId">The client that <c>RegisterClient</c> registered, which a refresh needs.</param>
/// <param name="ClientSecret">The secret of that client.</param>
/// <param name="RegistrationExpiresAt">When the registration of the client expires, as written.</param>
/// <param name="RefreshToken">The token that buys a new <paramref name="AccessToken"/>.</param>
internal sealed record SsoToken(
    string? StartUrl,
    string? Region,
    string? AccessToken,
    string? ExpiresAt,
    string? ClientId,
    string? ClientSecret,
    string? RegistrationExpiresAt,
    string? RefreshToken)
{
    /// <summary>Parses the content of a cache file.</summary>
    /// <param name="json">The content.</param>
    /// <returns>The token, or <see langword="null"/> if the content is not a JSON object.</returns>
    public static SsoToken? Parse(string json)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        try
        {
            // With all of the input at hand, the reader throws where it runs out of it, so Read never returns false here.
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            for (reader.Read(); reader.TokenType == JsonTokenType.PropertyName; reader.Read())
            {
                var name = reader.GetString()!;
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                {
                    values[name] = reader.GetString()!;
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // The exception may quote the file, which holds the token.
            return null;
        }

        return new SsoToken(
            values.GetValueOrDefault("startUrl"),
            values.GetValueOrDefault("region"),
            values.GetValueOrDefault("accessToken"),
            values.GetValueOrDefault("expiresAt"),
            values.GetValueOrDefault("clientId"),
            values.GetValueOrDefault("clientSecret"),
            values.GetValueOrDefault("registrationExpiresAt"),
            values.GetValueOrDefault("refreshToken"));
    }

    /// <summary>
    /// Parses a time of the file: ISO 8601, as the AWS CLI writes it (<c>2026-11-03T20:00:00Z</c>), and also with <c>UTC</c> in place of <c>Z</c>, which
    /// botocore's JSONFileCache writes when it formats a time with <c>%Z</c>.
    /// </summary>
    /// <param name="text">The time, or <see langword="null"/>.</param>
    /// <returns>The time, or <see langword="null"/> if it is missing or not a time.</returns>
    // Input: "2026-11-03T20:00:00UTC"   Result: 2026-11-03 20:00:00 +00:00
    public static DateTimeOffset? ParseTime(string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (text.EndsWith("UTC", StringComparison.Ordinal))
        {
            text = string.Concat(text.AsSpan(0, text.Length - 3), "Z");
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ? time : null;
    }

    /// <summary>Writes the token as botocore's <c>SSOTokenProvider</c> saves a refreshed one: its members in its order, and no member that is not set.</summary>
    /// <returns>The JSON.</returns>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            Write(writer, "startUrl", StartUrl);
            Write(writer, "region", Region);
            Write(writer, "accessToken", AccessToken);
            Write(writer, "expiresAt", ExpiresAt);
            Write(writer, "clientId", ClientId);
            Write(writer, "clientSecret", ClientSecret);
            Write(writer, "registrationExpiresAt", RegistrationExpiresAt);
            Write(writer, "refreshToken", RefreshToken);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Returns the name of the type: the generated text of a record would print the tokens.</summary>
    /// <returns>The name of the type.</returns>
    public override string ToString() => nameof(SsoToken);

    private static void Write(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
