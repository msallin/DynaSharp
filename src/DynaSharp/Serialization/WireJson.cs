using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// The JSON settings used on the wire, for request bodies, response bodies and <see cref="DynaSharp.AttributeValue.ToString"/>.
/// </summary>
internal static class WireJson
{
    /// <summary>
    /// Writer settings. Relaxed escaping leaves non-ASCII text and characters such as <c>&lt;</c> or <c>+</c>
    /// unescaped: compact and still valid JSON. "Unsafe" refers to embedding the output in HTML, which never happens.
    /// </summary>
    internal static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Reader settings. DynamoDB nests documents up to 32 levels and each level takes two JSON levels
    /// (<c>{"M":{...}}</c>), plus the response envelope. The default limit of 64 is too low for that.
    /// </summary>
    internal static readonly JsonReaderOptions ReaderOptions = new() { MaxDepth = 128 };

    /// <summary>Document settings with the depth of <see cref="ReaderOptions"/>, for a document of the wire or a <see cref="JsonElement"/> made from a value.</summary>
    internal static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = ReaderOptions.MaxDepth };

    // What goes into a JSON string as it is, and encodes to the same bytes with WriterOptions: printable ASCII except the quote and the
    // backslash. Everything else takes the encoder. Non-ASCII text would be legal as it is, but it needs the check for lone surrogates.
    private static readonly SearchValues<char> PlainCharacters = SearchValues.Create(" !#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~");

    private static readonly SearchValues<byte> PlainBytes = SearchValues.Create(" !#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~"u8);

    // Not IndexOfAnyInRange, which boxes until the JIT has optimized it, and this runs on every string of an item.
    private static readonly SearchValues<char> Surrogates = SearchValues.Create(AllSurrogates());

    /// <summary>Determines whether text goes into a JSON string as it is, one byte per character.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> if the writer would not change the text.</returns>
    internal static bool IsPlain(ReadOnlySpan<char> text) => !text.ContainsAnyExcept(PlainCharacters);

    /// <summary>
    /// Rejects text that is not valid UTF-16. <see cref="Utf8JsonWriter"/> turns a lone surrogate into U+FFFD without a word, so the value that is stored would not be the value that was
    /// written; <see cref="JsonEncodedText"/>, which the text of a request goes through, rejects it, and values get the same answer here.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="parameterName">The parameter that holds it.</param>
    /// <exception cref="ArgumentException">The text holds a lone surrogate.</exception>
    internal static void ThrowIfLoneSurrogate(ReadOnlySpan<char> text, string parameterName)
    {
        // A vectorized look that nearly all text ends with. Only text with a surrogate, which mostly means emoji, pays for the walk, and a pair that is fine costs no allocation.
        var first = text.IndexOfAny(Surrogates);
        if (first >= 0 && HasLoneSurrogate(text[first..]))
        {
            throw new ArgumentException("The text is not valid UTF-16: it holds a lone surrogate, which DynamoDB cannot store.", parameterName);
        }
    }

    /// <summary>Appends text as the inside of a JSON string, encoded as <see cref="WriterOptions"/> encodes it.</summary>
    /// <param name="output">The destination.</param>
    /// <param name="text">The text.</param>
    internal static void AppendEncoded(PooledByteBuffer output, ReadOnlySpan<char> text)
    {
        if (IsPlain(text))
        {
            output.AppendAscii(text);
        }
        else
        {
            output.Append(JsonEncodedText.Encode(text, WriterOptions.Encoder).EncodedUtf8Bytes);
        }
    }

    /// <summary>Appends UTF-8 text as the inside of a JSON string, encoded as <see cref="WriterOptions"/> encodes it.</summary>
    /// <param name="output">The destination.</param>
    /// <param name="utf8Text">The text.</param>
    internal static void AppendEncoded(PooledByteBuffer output, ReadOnlySpan<byte> utf8Text)
    {
        if (utf8Text.ContainsAnyExcept(PlainBytes))
        {
            output.Append(JsonEncodedText.Encode(utf8Text, WriterOptions.Encoder).EncodedUtf8Bytes);
        }
        else
        {
            output.Append(utf8Text);
        }
    }

    /// <summary>Creates a JSON writer with <see cref="WriterOptions"/>.</summary>
    /// <param name="output">The destination.</param>
    /// <returns>The writer. Dispose it (or call <see cref="Utf8JsonWriter.Flush"/>) before reading the output.</returns>
    internal static Utf8JsonWriter CreateWriter(IBufferWriter<byte> output) => new(output, WriterOptions);

    /// <summary>Writes JSON into a pooled buffer, for a value that is written in one form and read back in another.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">What the JSON is written from.</param>
    /// <param name="write">Writes the JSON.</param>
    /// <param name="initialCapacity">The minimum initial capacity of the buffer in bytes.</param>
    /// <returns>The buffer, flushed. The caller reads it and disposes it.</returns>
    internal static PooledByteBuffer Render<TState>(ref TState state, JsonWrite<TState> write, int initialCapacity = 256)
        where TState : allows ref struct
    {
        var buffer = new PooledByteBuffer(initialCapacity);
        try
        {
            using var json = CreateWriter(buffer);
            write(json, ref state);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        return buffer;
    }

    /// <summary>Writes JSON into a <see cref="JsonElement"/> that owns its memory.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">What the JSON is written from.</param>
    /// <param name="write">Writes one JSON value.</param>
    /// <returns>The element.</returns>
    internal static JsonElement ToElement<TState>(ref TState state, JsonWrite<TState> write)
        where TState : allows ref struct
    {
        using var buffer = Render(ref state, write);
        return JsonElement.Parse(buffer.WrittenSpan, DocumentOptions);
    }

    private static char[] AllSurrogates()
    {
        var all = new char[0xE000 - 0xD800];
        for (var i = 0; i < all.Length; i++)
        {
            all[i] = (char)(0xD800 + i);
        }

        return all;
    }

    private static bool HasLoneSurrogate(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }
}
