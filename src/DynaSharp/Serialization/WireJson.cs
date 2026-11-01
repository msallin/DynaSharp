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

    // What goes into a JSON string as it is, and encodes to the same bytes with WriterOptions: printable ASCII except the quote and the
    // backslash. Everything else takes the encoder. Non-ASCII text would be legal as it is, but it needs the check for lone surrogates.
    private static readonly SearchValues<char> PlainCharacters = SearchValues.Create(" !#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~");

    private static readonly SearchValues<byte> PlainBytes = SearchValues.Create(" !#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~"u8);

    /// <summary>Determines whether text goes into a JSON string as it is, one byte per character.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> if the writer would not change the text.</returns>
    internal static bool IsPlain(ReadOnlySpan<char> text) => !text.ContainsAnyExcept(PlainCharacters);

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
}
