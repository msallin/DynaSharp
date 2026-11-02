using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A position in a <c>Query</c> or <c>Scan</c>, for paging across requests: take <see cref="Page{T}.Next"/>, hand <see cref="ToString"/> to the client,
/// and on the next request pass <see cref="Parse"/> of what came back to <c>PageAsync</c>. The text is safe in a URL. It is encoded, not encrypted and not signed, so a client can read it and choose its position in the partition. The
/// default value is the start of the read.
/// </summary>
/// <remarks>
/// <para>
/// Treat the text as opaque, but not as secret: it carries the key values of the last item that was read, as <c>LastEvaluatedKey</c> does in every SDK.
/// Encrypt or sign the token if the keys of the table hold what a client must not see, or if the position of a read must not be up to the client.
/// </para>
/// <para>
/// A token belongs to the query it came from: the same table or index and the same key condition. DynamoDB rejects a key that does not fit with a
/// <see cref="ValidationException"/>. A token does not expire, and the item of its key need not exist any more: a read continues from the position of the key.
/// </para>
/// <para>
/// The first byte behind the text is the version of its format, for page and fan-out tokens alike. A token of a format this release does not know, older or newer, fails in <see cref="Parse"/> with a
/// <see cref="FormatException"/> that says so, and the client starts its read again. Nothing guesses, because a key read the wrong way would be a wrong position with no error. A token has no limit on its
/// length: one of a page is as long as a key of the table, and one of a fan-out grows with its partitions.
/// </para>
/// <para>
/// A token of a fan-out query holds two keys per partition, where it continues and what comes next, or the fact that the partition is done, so it is as long as the list of partitions.
/// It holds the direction of the read as well. It goes through <see cref="Parse"/> like any other. <c>PageAsync</c> of the fan-out then checks it against the number of its partitions, its direction and the
/// keys of the read, with a <see cref="FormatException"/> as from <see cref="Parse"/>, and DynamoDB checks the start keys.
/// </para>
/// <para>Two tokens are equal if their text is. <see cref="ToKey"/> and <see cref="FromKey"/> are the bridge to <c>ExclusiveStartKey</c> and <c>LastEvaluatedKey</c> of the request API.</para>
/// </remarks>
public readonly record struct ContinuationToken
{
    // When what a token holds changes, for a page or a fan-out: raise this, and teach Inspect the old format if it is to stay readable. The JSON of a key starts
    // with a brace, so a token from before there was a version reads as version 123, and is no token of this one.
    private const byte FormatVersion = 1;

    // The version and the key as JSON in Base64Url, so that a token costs one string, and a read that sends it decodes it into the request without a dictionary.
    private readonly string? _text;

    private ContinuationToken(string text)
    {
        _text = text;
    }

    private enum TokenText
    {
        Valid,
        NotAToken,
        OtherVersion,
    }

    /// <summary>Gets a value indicating whether this token holds a position. The default value does not: it stands for the start of a read, and for the end in <see cref="Page{T}.Next"/>.</summary>
    public bool HasValue => _text is not null;

    /// <summary>
    /// Reads a token from its text. <see langword="null"/> and an empty string are the default value, which is the start of a read, so the first request of a client, which has no token,
    /// needs no check, and <see cref="ToString"/> always round-trips.
    /// </summary>
    /// <param name="text">What <see cref="ToString"/> returned, or nothing.</param>
    /// <returns>The token.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> is not the text of a token, or of a token of a format this version of the library does not read. The message does not quote it, because it comes from a client.</exception>
    public static ContinuationToken Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        return Inspect(text) switch
        {
            TokenText.Valid => new ContinuationToken(text),
            TokenText.OtherVersion => throw new FormatException("This continuation token has a format this version of the library does not read: it was written by another version, or it was altered on the way. Start the read again."),
            _ => throw new FormatException("This is not a continuation token: it did not come from Next.ToString() of a page, or it was cut short or altered on the way."),
        };
    }

    /// <summary>
    /// Reads a token from its text, as <see cref="Parse"/> does, so <see langword="null"/> and an empty string are the default value. Everything a token holds goes into a request, so the text
    /// must decode to the version of the format and exactly one key of attribute values; anything else in it is rejected here, whatever a client sends.
    /// </summary>
    /// <param name="text">What <see cref="ToString"/> returned, or nothing.</param>
    /// <param name="token">The token, or the default value if the text is not one.</param>
    /// <returns><see langword="true"/> if the text is a token or there is none.</returns>
    public static bool TryParse(string? text, out ContinuationToken token)
    {
        token = default;
        if (!string.IsNullOrEmpty(text))
        {
            if (Inspect(text) != TokenText.Valid)
            {
                return false;
            }

            token = new ContinuationToken(text);
        }

        return true;
    }

    /// <summary>
    /// Creates a token from a key of the request API, which continues in a command what a request started: <c>ContinuationToken.FromKey(response.LastEvaluatedKey!)</c>.
    /// An <see cref="Item"/> works as well. The attributes are copied.
    /// </summary>
    /// <param name="key">The key, as <c>LastEvaluatedKey</c> holds it.</param>
    /// <returns>The token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>. A missing <c>LastEvaluatedKey</c> means the end of the read, so there is no position to continue from.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> has no attributes.</exception>
    public static ContinuationToken FromKey(IReadOnlyDictionary<string, AttributeValue> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Count == 0)
        {
            throw new ArgumentException("A key has attributes. An empty one is no position in a read.", nameof(key));
        }

        using var buffer = new PooledByteBuffer();
        using var json = WireJson.CreateWriter(buffer);
        AttributeValueConverter.WriteItem(new DynamoDbWriter(json), key);
        json.Flush();
        return FromRawKey(buffer.WrittenSpan);
    }

    /// <summary>Creates the key of the request API from this token, which continues in a request what a command started: <c>ExclusiveStartKey = token.ToKey()</c>.</summary>
    /// <returns>The key, a new item, or <see langword="null"/> for the default value, which is no position.</returns>
    public Item? ToKey()
    {
        if (_text is null)
        {
            return null;
        }

        using var buffer = new PooledByteBuffer();
        CopyKeyTo(buffer);
        var reader = new DynamoDbReader(buffer.WrittenSpan);
        return AttributeValueConverter.ReadItem(ref reader);
    }

    /// <summary>Gets the text of the token, to hand to a client.</summary>
    /// <returns>The text, which <see cref="Parse"/> reads, or an empty string for the default value.</returns>
    public override string ToString() => _text ?? string.Empty;

    /// <summary>Creates a token from the raw JSON of a key.</summary>
    /// <param name="utf8Key">The key.</param>
    /// <returns>The token.</returns>
    internal static ContinuationToken FromRawKey(ReadOnlySpan<byte> utf8Key)
    {
        var length = utf8Key.Length + 1;
        var bytes = ArrayPool<byte>.Shared.Rent(length);
        bytes[0] = FormatVersion;
        utf8Key.CopyTo(bytes.AsSpan(1));
        var text = Base64Url.EncodeToString(bytes.AsSpan(0, length));
        ArrayPool<byte>.Shared.Return(bytes);
        return new ContinuationToken(text);
    }

    /// <summary>Writes the raw JSON of the key to a buffer. The default value writes nothing.</summary>
    /// <param name="destination">The buffer.</param>
    internal void CopyKeyTo(PooledByteBuffer destination)
    {
        if (_text is not null)
        {
            var decoded = destination.GetSpan(Base64Url.GetMaxDecodedLength(_text.Length));
            Base64Url.DecodeFromChars(_text, decoded, out _, out var written);
            decoded[1..written].CopyTo(decoded);
            destination.Advance(written - 1);
        }
    }

    // A token ends up in the body of a request as it is, and it can come from anyone. One JSON object of attribute values, and nothing behind it, is what
    // cannot close the object around it and add members of its own, such as another TableName.
    private static TokenText Inspect(string text)
    {
        using var buffer = new PooledByteBuffer();
        if (Base64Url.DecodeFromChars(text, buffer.GetSpan(Base64Url.GetMaxDecodedLength(text.Length)), out _, out var written) != OperationStatus.Done || written == 0)
        {
            return TokenText.NotAToken;
        }

        buffer.Advance(written);
        if (buffer.WrittenSpan[0] != FormatVersion)
        {
            return TokenText.OtherVersion;
        }

        try
        {
            var reader = new DynamoDbReader(buffer.WrittenSpan[1..]);
            return AttributeValueConverter.ReadItem(ref reader).Count > 0 && !reader.Json.Read() ? TokenText.Valid : TokenText.NotAToken;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return TokenText.NotAToken;
        }
    }
}
