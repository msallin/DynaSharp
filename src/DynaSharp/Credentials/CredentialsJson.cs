using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DynaSharp.Credentials;

/// <summary>
/// The members of a JSON object that a credentials endpoint, a <c>credential_process</c> or the token cache of the AWS CLI writes: its strings and
/// numbers by name, read with <see cref="Utf8JsonReader"/> and without reflection. A value of another type counts as missing, and of a name given
/// twice the last counts. What these documents share besides: the parse of a time and the message about missing fields.
/// </summary>
internal sealed class CredentialsJson
{
    private readonly Dictionary<string, (bool IsNumber, string Text)> _members;

    private CredentialsJson(Dictionary<string, (bool IsNumber, string Text)> members)
    {
        _members = members;
    }

    /// <summary>Reads the members of a JSON object, or of the object that one of its members holds.</summary>
    /// <param name="json">The UTF-8 document.</param>
    /// <param name="within">
    /// The member whose object to read, such as <c>roleCredentials</c>, or <see langword="null"/> for the document itself. A document without that
    /// member, or with another value in it, has no members.
    /// </param>
    /// <returns>
    /// The members, or <see langword="null"/> if the document is no JSON object. Never throws: the message of a parse error may quote the document,
    /// which holds secrets.
    /// </returns>
    public static CredentialsJson? TryRead(ReadOnlySpan<byte> json, string? within = null)
    {
        var members = new Dictionary<string, (bool IsNumber, string Text)>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(json);
        try
        {
            // With all of the input at hand, the reader throws where it runs out of it, so Read never returns false here.
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            ReadMembers(ref reader, members, within);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: a string that is not valid UTF-8, which the reader notices only when it decodes the string.
            return null;
        }

        return new CredentialsJson(members);
    }

    /// <summary>
    /// Parses a time of a credentials document or of <c>AWS_CREDENTIAL_EXPIRATION</c>: ISO 8601 (<c>2026-11-03T20:00:00Z</c>), and also with <c>UTC</c> in
    /// place of <c>Z</c>, which botocore's JSONFileCache writes when it formats a time with <c>%Z</c>. A time without an offset is UTC.
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

    /// <summary>Names the fields that are missing, for a message that shows no value.</summary>
    /// <param name="fields">Each field with whether it is missing.</param>
    /// <returns>The missing ones, such as <c>SecretAccessKey, Expiration</c>, or <see langword="null"/> if none is.</returns>
    public static string? DescribeMissing(params ReadOnlySpan<(string Name, bool Missing)> fields)
    {
        StringBuilder? missing = null;
        foreach (var (name, isMissing) in fields)
        {
            if (isMissing)
            {
                missing = missing is null ? new StringBuilder(name) : missing.Append(", ").Append(name);
            }
        }

        return missing?.ToString();
    }

    /// <summary>Gets a member that is a string.</summary>
    /// <param name="name">The name of the member.</param>
    /// <returns>The string, or <see langword="null"/> if the member is missing or of another type.</returns>
    public string? GetString(string name) => _members.TryGetValue(name, out var member) && !member.IsNumber ? member.Text : null;

    /// <summary>Gets a member that is a whole number.</summary>
    /// <param name="name">The name of the member.</param>
    /// <param name="value">The number, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the member is a number without fraction or exponent that fits a <see cref="long"/>.</returns>
    public bool TryGetInt64(string name, out long value)
    {
        value = 0;
        return _members.TryGetValue(name, out var member) && member.IsNumber && long.TryParse(member.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Gets a member that is a number.</summary>
    /// <param name="name">The name of the member.</param>
    /// <param name="value">The number, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the member is a number.</returns>
    public bool TryGetDouble(string name, out double value)
    {
        value = 0;
        return _members.TryGetValue(name, out var member) && member.IsNumber && double.TryParse(member.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // The reader is on the brace of an object; reads its members up to the closing brace. On a name, Skip passes its value too.
    private static void ReadMembers(ref Utf8JsonReader reader, Dictionary<string, (bool IsNumber, string Text)> members, string? within)
    {
        for (reader.Read(); reader.TokenType == JsonTokenType.PropertyName; reader.Read())
        {
            var name = reader.GetString()!;
            reader.Read();
            if (within is not null)
            {
                if (name == within && reader.TokenType == JsonTokenType.StartObject)
                {
                    ReadMembers(ref reader, members, null);
                }
                else
                {
                    reader.Skip();
                }
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                members[name] = (false, reader.GetString()!);
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                members[name] = (true, Encoding.UTF8.GetString(reader.ValueSpan));
            }
            else
            {
                reader.Skip();
            }
        }
    }
}
