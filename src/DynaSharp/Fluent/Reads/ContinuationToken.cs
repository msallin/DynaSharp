using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Text.Json;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A position in a <c>Query</c> or <c>Scan</c>, for paging across requests: take <see cref="Page{T}.Next"/>, hand <see cref="ToString"/> to the client,
/// and on the next request pass <see cref="Parse"/> of what came back to <c>PageAsync</c>. The text is safe in a URL. It is encoded, not encrypted, and signed only by a client with
/// <see cref="DynamoDbClientOptions.TokenSigner"/>: without one, a client can read it and choose its position in the partition. The default value is the start of the read.
/// </summary>
/// <remarks>
/// <para>
/// Treat the text as opaque, but not as secret: it carries the key values of the last item that was read, as <c>LastEvaluatedKey</c> does in every SDK, signed or not.
/// Encrypt the token if the keys of the table hold what a client must not see. Sign it, with <see cref="DynamoDbClientOptions.TokenSigner"/>, if the position of a read must not be up to the client.
/// Inside the key condition a position a client picks is only a place among items it may read anyway, and a filter applies from any position, so signing matters where a key condition is all
/// that keeps clients apart from each other's items in a partition they share.
/// </para>
/// <para>
/// A signed token is a format of its own, version 2: the version, the key and an HMAC-SHA256 of 32 bytes. <see cref="Parse"/> takes both formats and checks the shape only, because it has no keys.
/// <c>PageAsync</c> on a client that signs checks the signature, and rejects a plain token that came as text; on a client that does not sign, it rejects a signed one. Both fail with a
/// <see cref="FormatException"/>, before a request goes out. See <see cref="ContinuationTokenSigner"/> for what the signature binds.
/// </para>
/// <para>
/// A token belongs to the query it came from: the same table or index and the same key condition. DynamoDB rejects a key that does not fit with a
/// <see cref="ValidationException"/>. A token does not expire, and the item of its key need not exist any more: a read continues from the position of the key.
/// </para>
/// <para>
/// The first byte behind the text is the version of its format, for page and fan-out tokens alike. A token of a format this release does not know, older or newer, fails in <see cref="Parse"/> with a
/// <see cref="FormatException"/> that says so, and the client starts its read again. A patch release reads and writes the formats of the release before it; one that changes a format says so in its notes, so in a rolling deployment across it a client whose request reaches the other release starts its read again. Nothing guesses, because a key read the wrong way would be a wrong position with no error. A token has no limit on its
/// length: one of a page is as long as a key of the table, and one of a fan-out grows with its partitions.
/// </para>
/// <para>
/// A token of a fan-out query holds two keys per partition, where it continues and what comes next, or the partition key of a partition that is done, so it is as long as the list of partitions.
/// It holds the direction of the read as well. It goes through <see cref="Parse"/> like any other. <c>PageAsync</c> of the fan-out then checks it against the number and the order of its partitions, its direction and the
/// keys of the read, with a <see cref="FormatException"/> as from <see cref="Parse"/>, and DynamoDB checks the start keys.
/// </para>
/// <para>Two tokens are equal if their text is. <see cref="ToKey"/> and <see cref="FromKey"/> are the bridge to <c>ExclusiveStartKey</c> and <c>LastEvaluatedKey</c> of the request API.</para>
/// </remarks>
public readonly record struct ContinuationToken
{
    // When what a token holds changes, for a page or a fan-out: raise these, and teach Inspect the old format if it is to stay readable. The JSON of a key starts
    // with a brace, so a token from before there was a version reads as version 123, and is no token of this one.
    // Plain:  [1][key JSON]
    // Signed: [2][key JSON][HMAC-SHA256 of Signed, the length of the JSON, the JSON and the binding of the read, 32 bytes]
    private const byte FormatVersion = 1;
    private const byte SignedFormatVersion = 2;

    private const string SignatureFailed = "This continuation token does not carry a signature of this service for this read: it was made up or altered, it belongs to another query, or the key that signed it is no longer held. Start the read again.";

    // The version and the key as JSON in Base64Url, so that a token costs one string, and a read that sends it decodes it into the request without a dictionary.
    private readonly string? _text;
    private readonly TokenForm _form;

    private ContinuationToken(string text, TokenForm form)
    {
        _text = text;
        _form = form;
    }

    private enum TokenText
    {
        Valid,
        NotAToken,
        OtherVersion,
    }

    // A plain token that this process made, from a page or from a key of its own, is no input of a client, so a client that signs takes it as it is. One that came as text is.
    private enum TokenForm : byte
    {
        Plain,
        PlainText,
        Signed,
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

        return Inspect(text, out var form) switch
        {
            TokenText.Valid => new ContinuationToken(text, form),
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
            if (Inspect(text, out var form) != TokenText.Valid)
            {
                return false;
            }

            token = new ContinuationToken(text, form);
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

    /// <summary>Tells whether two tokens are the same position: whether their text is the same, wherever they came from.</summary>
    /// <param name="other">The other token.</param>
    /// <returns><see langword="true"/> if the text of both is the same.</returns>
    public bool Equals(ContinuationToken other) => string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => _text is null ? 0 : _text.GetHashCode(StringComparison.Ordinal);

    /// <summary>Creates a token from the raw JSON of a key, which this process made, so that a client that signs takes it.</summary>
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
        return new ContinuationToken(text, TokenForm.Plain);
    }

    /// <summary>Creates the token of the next page of a read: signed if the client signs, and plain as from <see cref="FromRawKey(ReadOnlySpan{byte})"/> if it does not.</summary>
    /// <param name="utf8Key">The key.</param>
    /// <param name="state">The read, whose table, index and key condition the signature binds.</param>
    /// <param name="plan">The plan of a fan-out, whose partitions the signature binds as well, or <see langword="null"/>.</param>
    /// <returns>The token.</returns>
    internal static ContinuationToken FromRawKey(ReadOnlySpan<byte> utf8Key, CommandState state, FanOutPlan? plan)
    {
        if (state.Table.Core.Options.TokenSigner is not { } signer)
        {
            return FromRawKey(utf8Key);
        }

        // What is signed is [2][key][binding]. The token is [2][key][signature]: the read that checks it writes the binding again.
        using var buffer = new PooledByteBuffer();
        buffer.Append(SignedFormatVersion);
        buffer.Append(utf8Key);
        var tokenLength = buffer.WrittenCount;
        WriteBinding(buffer, utf8Key.Length, state, plan);
        Span<byte> signature = stackalloc byte[ContinuationTokenSigner.SignatureLength];
        signer.Sign(buffer.WrittenSpan, signature);
        buffer.Truncate(tokenLength);
        buffer.Append(signature);
        return new ContinuationToken(Base64Url.EncodeToString(buffer.WrittenSpan), TokenForm.Signed);
    }

    /// <summary>
    /// Checks that a read may continue from this token, before a request goes out: with a signature of the client for this read if the client signs, and without one if it does not.
    /// A token that this process made without one goes through, so that <see cref="FromKey"/> continues a read of the request API on a client that signs.
    /// </summary>
    /// <param name="state">The read.</param>
    /// <param name="plan">The plan of a fan-out, or <see langword="null"/>.</param>
    /// <exception cref="FormatException">The token does not fit what the client says about signatures, or its signature does not hold for this read.</exception>
    internal void ThrowIfNotFor(CommandState state, FanOutPlan? plan)
    {
        if (_text is null)
        {
            return;
        }

        var signer = state.Table.Core.Options.TokenSigner;
        if (_form != TokenForm.Signed)
        {
            if (signer is not null && _form == TokenForm.PlainText)
            {
                throw new FormatException("This continuation token is not signed, and this client signs its tokens: it came from a client without TokenSigner, or it was made by hand. Start the read again.");
            }

            return;
        }

        if (signer is null)
        {
            throw new FormatException("This continuation token is signed, and this client has no TokenSigner to check it with: it came from a client that signs, or from another service. Start the read again.");
        }

        using var buffer = new PooledByteBuffer();
        var decoded = buffer.GetSpan(Base64Url.GetMaxDecodedLength(_text.Length));
        Base64Url.DecodeFromChars(_text, decoded, out _, out var written);
        var tokenLength = written - ContinuationTokenSigner.SignatureLength;
        Span<byte> signature = stackalloc byte[ContinuationTokenSigner.SignatureLength];
        decoded[tokenLength..written].CopyTo(signature);
        buffer.Advance(tokenLength);
        WriteBinding(buffer, tokenLength - 1, state, plan);
        if (!signer.Verify(buffer.WrittenSpan, signature))
        {
            throw new FormatException(SignatureFailed);
        }
    }

    /// <summary>Writes the raw JSON of the key to a buffer. The default value writes nothing.</summary>
    /// <param name="destination">The buffer.</param>
    internal void CopyKeyTo(PooledByteBuffer destination)
    {
        if (_text is not null)
        {
            var decoded = destination.GetSpan(Base64Url.GetMaxDecodedLength(_text.Length));
            Base64Url.DecodeFromChars(_text, decoded, out _, out var written);
            var end = _form == TokenForm.Signed ? written - ContinuationTokenSigner.SignatureLength : written;
            decoded[1..end].CopyTo(decoded);
            destination.Advance(end - 1);
        }
    }

    // What a signature holds besides the token: the length of the key, so that no bytes of the key can pass for the binding, and what makes a token one of its read. Each part
    // is JSON, which holds no zero byte, so a zero ends it.
    // [length of the key, 4 bytes]DynaSharp.ContinuationToken\0{"TableName":"Orders","IndexName":"ByStatus"\0#"pk" = :{"S":"a"} AND begins_with(#"sk", :{"S":"b"})\0[{"S":"p1"},{"S":"p2"}]
    private static void WriteBinding(PooledByteBuffer buffer, int keyLength, CommandState state, FanOutPlan? plan)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer.GetSpan(sizeof(int)), keyLength);
        buffer.Advance(sizeof(int));
        buffer.Append("DynaSharp.ContinuationToken\0"u8);
        buffer.Append(state.Table.BodyStart);
        buffer.Append((byte)0);
        state.Expressions.WriteResolvedKeyCondition(buffer);
        if (plan is not null)
        {
            buffer.Append((byte)0);
            using var json = WireJson.CreateWriter(buffer);
            json.WriteStartArray();
            foreach (var partitionKey in plan.PartitionKeys)
            {
                AttributeValueConverter.Write(new DynamoDbWriter(json), partitionKey);
            }

            json.WriteEndArray();
            json.Flush();
        }
    }

    // A token ends up in the body of a request as it is, and it can come from anyone. One JSON object of attribute values, and nothing behind it, is what
    // cannot close the object around it and add members of its own, such as another TableName. A signed token is checked here for its shape only: the client that reads with it checks the signature.
    private static TokenText Inspect(string text, out TokenForm form)
    {
        form = TokenForm.PlainText;
        using var buffer = new PooledByteBuffer();
        if (Base64Url.DecodeFromChars(text, buffer.GetSpan(Base64Url.GetMaxDecodedLength(text.Length)), out _, out var written) != OperationStatus.Done || written == 0)
        {
            return TokenText.NotAToken;
        }

        buffer.Advance(written);
        var bytes = buffer.WrittenSpan;
        if (bytes[0] == SignedFormatVersion)
        {
            form = TokenForm.Signed;
            if (written <= 1 + ContinuationTokenSigner.SignatureLength)
            {
                return TokenText.NotAToken;
            }

            bytes = bytes[..^ContinuationTokenSigner.SignatureLength];
        }
        else if (bytes[0] != FormatVersion)
        {
            return TokenText.OtherVersion;
        }

        try
        {
            var reader = new DynamoDbReader(bytes[1..]);
            return AttributeValueConverter.ReadItem(ref reader).Count > 0 && !reader.Json.Read() ? TokenText.Valid : TokenText.NotAToken;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return TokenText.NotAToken;
        }
    }
}
