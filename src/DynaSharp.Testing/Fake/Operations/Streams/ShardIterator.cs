using System.Globalization;
using System.Text;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A shard iterator of the fake: a position in the shard of a stream and the time it was handed out, which the fake keeps nowhere but in the iterator itself, so an iterator costs
/// nothing to keep or to throw away.
/// </summary>
/// <remarks>
/// The text is <c>FakeShardIterator/</c> and the Base64 of <c>&lt;stream ARN&gt;|&lt;shard id&gt;|&lt;position&gt;|&lt;Unix milliseconds&gt;</c>. It is opaque to a reader, as an
/// iterator of DynamoDB is; a text that does not decode, such as one a test made up, is no iterator.
/// </remarks>
/// <param name="StreamArn">The stream.</param>
/// <param name="ShardId">The shard.</param>
/// <param name="Position">The index into the records of the shard of the next record to read.</param>
/// <param name="IssuedAt">When the fake handed it out.</param>
internal sealed record ShardIterator(string StreamArn, string ShardId, int Position, DateTimeOffset IssuedAt)
{
    /// <summary>How long an iterator is valid after the fake handed it out.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private const string Prefix = "FakeShardIterator/";
    private const char Separator = '|';

    /// <summary>Reads an iterator a request sent.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The iterator, or <see langword="null"/> if the text is none of the fake.</returns>
    public static ShardIterator? Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var bytes = new byte[text.Length];
        if (!Convert.TryFromBase64String(text[Prefix.Length..], bytes, out var written))
        {
            return null;
        }

        var parts = Encoding.UTF8.GetString(bytes, 0, written).Split(Separator);
        if (parts.Length != 4
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || milliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return null;
        }

        return new ShardIterator(parts[0], parts[1], position, DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
    }

    /// <summary>Writes the iterator as the text a response carries.</summary>
    /// <returns>The text.</returns>
    public string Encode() =>
        Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{StreamArn}{Separator}{ShardId}{Separator}{Position}{Separator}{IssuedAt.ToUnixTimeMilliseconds()}")));
}
