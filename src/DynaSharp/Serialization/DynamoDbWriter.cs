using System.Numerics;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// Writes DynamoDB JSON: attribute values in their typed form, such as <c>{"S":"abc"}</c> or <c>{"N":"42"}</c>, and
/// items, which are JSON objects that map attribute names to attribute values.
/// </summary>
/// <remarks>
/// <para>
/// This is the only code that produces DynamoDB JSON. <see cref="AttributeValue"/>, entities mapped by the source
/// generator and custom converters (<see cref="IDynamoDbConverter{T}"/>) all write through it, so each rule of the
/// format is implemented once: numbers travel as text, binary values as base64, null as <c>{"NULL":true}</c>.
/// </para>
/// <para>
/// Each method that writes a value writes one complete attribute value, type descriptor included. Inside a map or an
/// item, write the attribute name first; inside a list, write the values one after another. Lists, maps, sets and
/// items are written with matching start and end calls:
/// </para>
/// <code>
/// writer.WriteStartItem();
/// writer.WriteAttributeName("pk"u8);
/// writer.WriteString("USER#42");
/// writer.WriteAttributeName("roles"u8);
/// writer.WriteStartStringSet();
/// writer.WriteStringSetElement("admin");
/// writer.WriteEndSet();
/// writer.WriteEndItem();
/// // {"pk":{"S":"USER#42"},"roles":{"SS":["admin"]}}
/// </code>
/// <para>
/// Calls that break the JSON structure, such as a value inside a map without an attribute name, throw
/// <see cref="InvalidOperationException"/>. DynamoDB rules beyond the JSON structure are enforced by DynamoDB, which
/// rejects the request with a validation error: for example an empty set, a set with duplicates, or an item larger
/// than 400 KB.
/// </para>
/// </remarks>
public readonly ref struct DynamoDbWriter
{
    // Fits every built-in number type: decimal needs at most 31 characters, double 24. Longer text means an
    // arbitrary-precision type such as BigInteger.
    private const int StackNumberLength = 64;

    private static readonly JsonEncodedText StringDescriptor = JsonEncodedText.Encode("S"u8);
    private static readonly JsonEncodedText NumberDescriptor = JsonEncodedText.Encode("N"u8);
    private static readonly JsonEncodedText BinaryDescriptor = JsonEncodedText.Encode("B"u8);
    private static readonly JsonEncodedText BooleanDescriptor = JsonEncodedText.Encode("BOOL"u8);
    private static readonly JsonEncodedText NullDescriptor = JsonEncodedText.Encode("NULL"u8);
    private static readonly JsonEncodedText ListDescriptor = JsonEncodedText.Encode("L"u8);
    private static readonly JsonEncodedText MapDescriptor = JsonEncodedText.Encode("M"u8);
    private static readonly JsonEncodedText StringSetDescriptor = JsonEncodedText.Encode("SS"u8);
    private static readonly JsonEncodedText NumberSetDescriptor = JsonEncodedText.Encode("NS"u8);
    private static readonly JsonEncodedText BinarySetDescriptor = JsonEncodedText.Encode("BS"u8);

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbWriter"/> struct over a JSON writer. DynaSharp creates writers for its
    /// own requests; use this to test a custom converter. Flush or dispose <paramref name="json"/> before reading its output.
    /// </summary>
    /// <param name="json">The destination.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    public DynamoDbWriter(Utf8JsonWriter json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Json = json;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbWriter"/> struct that writes a value that stands alone directly to the buffer
    /// behind <paramref name="json"/>, which only the commands of the table API do. Strings, numbers and booleans that need no encoding skip
    /// the JSON writer, which is most of what it costs to build the body of a command. The output is the same, byte for byte, and this
    /// stays the only code that knows the format. What a list or map holds goes through <paramref name="json"/>.
    /// </summary>
    /// <param name="json">The JSON writer on <paramref name="direct"/>, which must hold nothing that is not flushed.</param>
    /// <param name="direct">The buffer.</param>
    internal DynamoDbWriter(Utf8JsonWriter json, PooledByteBuffer direct)
        : this(json)
    {
        Direct = direct;
    }

    /// <summary>Gets the underlying JSON writer, for request envelopes around the attribute values.</summary>
    internal Utf8JsonWriter Json { get; }

    private PooledByteBuffer? Direct { get; }

    private bool IsForDisplay { get; init; }

    // Inside a list or map the JSON writer holds what came before, and the order of the output is its business.
    private PooledByteBuffer? DirectBuffer => Json.CurrentDepth == 0 ? Direct : null;

    /// <summary>Writes the name of the next attribute in a map or item.</summary>
    /// <param name="utf8Name">The attribute name as UTF-8, typically a literal such as <c>"pk"u8</c>.</param>
    public void WriteAttributeName(ReadOnlySpan<byte> utf8Name) => Json.WritePropertyName(utf8Name);

    /// <summary>Writes the name of the next attribute in a map or item.</summary>
    /// <param name="name">The attribute name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> holds a lone surrogate, which is not text.</exception>
    public void WriteAttributeName(string name)
    {
        ThrowIfNoText(name, nameof(name));
        Json.WritePropertyName(name);
    }

    /// <summary>Writes a string value, <c>{"S":"..."}</c>.</summary>
    /// <param name="value">The string. Empty strings are allowed except in key attributes, which DynamoDB validates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>; write <see cref="WriteNull"/> instead.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> holds a lone surrogate, which is not text, and would otherwise be stored as U+FFFD.</exception>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (DirectBuffer is { } direct && WireJson.IsPlain(value))
        {
            WritePlain(direct, "{\"S\":\""u8, value);
            return;
        }

        ThrowIfNoText(value, nameof(value));
        Json.WriteStartObject();
        Json.WriteString(StringDescriptor, value);
        Json.WriteEndObject();
    }

    /// <summary>Writes a string value, <c>{"S":"..."}</c>, from UTF-8 text, for example formatted into a stack buffer.</summary>
    /// <param name="utf8Value">The string as UTF-8. Invalid UTF-8 sequences are replaced with U+FFFD.</param>
    public void WriteString(ReadOnlySpan<byte> utf8Value)
    {
        Json.WriteStartObject();
        Json.WriteString(StringDescriptor, utf8Value);
        Json.WriteEndObject();
    }

    /// <summary>
    /// Writes a number value, <c>{"N":"..."}</c>, formatted with the invariant culture. A <see cref="char"/> counts as a number type in .NET but
    /// not here: it is written as the string value it is, as everywhere in DynaSharp.
    /// </summary>
    /// <typeparam name="T">Any .NET number type, for example <see cref="int"/>, <see cref="decimal"/> or <see cref="double"/>.</typeparam>
    /// <param name="value">
    /// The number. DynamoDB stores up to 38 significant digits, with a magnitude of zero or from 1E-130 to 9.9999999999999999999999999999999999999E+125.
    /// It rejects anything else with a validation error; a <see cref="double"/> can be outside that range.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite; DynamoDB cannot store either.</exception>
    public void WriteNumber<T>(T value)
        where T : INumberBase<T>
    {
        // Every path of a value with a generic number type ends here, so this one test keeps {"N":"a"} from being written.
        if (typeof(T) == typeof(char))
        {
            WriteString(DynamoDbNumber.Format(value));
            return;
        }

        DynamoDbNumber.ThrowIfNotFinite(value);
        if (DirectBuffer is { } direct && TryWriteNumberDirect(direct, value))
        {
            return;
        }

        Json.WriteStartObject();
        Json.WritePropertyName(NumberDescriptor);
        WriteNumberText(value);
        Json.WriteEndObject();
    }

    /// <summary>Writes a number value, <c>{"N":"..."}</c>, from its textual representation, sent unchanged.</summary>
    /// <param name="value">The number as text, for example <c>"42"</c> or <c>"-1.5E-10"</c>. DynamoDB validates the format.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public void WriteNumber(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (DirectBuffer is { } direct && WireJson.IsPlain(value))
        {
            WritePlain(direct, "{\"N\":\""u8, value);
            return;
        }

        Json.WriteStartObject();
        Json.WriteString(NumberDescriptor, value);
        Json.WriteEndObject();
    }

    /// <summary>Writes a binary value, <c>{"B":"..."}</c>, encoded as base64.</summary>
    /// <param name="value">The bytes, possibly none (allowed except in key attributes).</param>
    public void WriteBinary(ReadOnlySpan<byte> value)
    {
        Json.WriteStartObject();
        Json.WriteBase64String(BinaryDescriptor, value);
        Json.WriteEndObject();
    }

    /// <summary>Writes a boolean value, <c>{"BOOL":true}</c> or <c>{"BOOL":false}</c>.</summary>
    /// <param name="value">The boolean.</param>
    public void WriteBool(bool value)
    {
        if (DirectBuffer is { } direct)
        {
            direct.Append(value ? "{\"BOOL\":true}"u8 : "{\"BOOL\":false}"u8);
            return;
        }

        Json.WriteStartObject();
        Json.WriteBoolean(BooleanDescriptor, value);
        Json.WriteEndObject();
    }

    /// <summary>Writes the null value, <c>{"NULL":true}</c>.</summary>
    /// <remarks>
    /// An attribute with the null value exists in the item; it is not the same as a missing attribute. To leave an
    /// attribute out, do not write it at all.
    /// </remarks>
    public void WriteNull()
    {
        Json.WriteStartObject();
        Json.WriteBoolean(NullDescriptor, true);
        Json.WriteEndObject();
    }

    /// <summary>Starts a list value, <c>{"L":[</c>. Write the elements as values, then call <see cref="WriteEndList"/>.</summary>
    /// <remarks>Elements may have different types. Empty lists are allowed.</remarks>
    public void WriteStartList()
    {
        Json.WriteStartObject();
        Json.WriteStartArray(ListDescriptor);
    }

    /// <summary>Ends a list value started with <see cref="WriteStartList"/>.</summary>
    public void WriteEndList()
    {
        Json.WriteEndArray();
        Json.WriteEndObject();
    }

    /// <summary>
    /// Starts a map value, <c>{"M":{</c>. Write attribute names and values, then call <see cref="WriteEndMap"/>.
    /// </summary>
    /// <remarks>Empty maps are allowed. Attribute names must be unique within the map.</remarks>
    public void WriteStartMap()
    {
        Json.WriteStartObject();
        Json.WriteStartObject(MapDescriptor);
    }

    /// <summary>Ends a map value started with <see cref="WriteStartMap"/>.</summary>
    public void WriteEndMap()
    {
        Json.WriteEndObject();
        Json.WriteEndObject();
    }

    /// <summary>
    /// Starts a string set, <c>{"SS":[</c>. Write the elements with <see cref="WriteStringSetElement(string)"/>, then call
    /// <see cref="WriteEndSet"/>.
    /// </summary>
    /// <remarks>DynamoDB rejects empty sets and sets with duplicates. Write a set attribute only if it has elements.</remarks>
    public void WriteStartStringSet() => WriteStartSet(StringSetDescriptor);

    /// <summary>
    /// Starts a number set, <c>{"NS":[</c>. Write the elements with <see cref="WriteNumberSetElement{T}(T)"/> or
    /// <see cref="WriteNumberSetElement(string)"/>, then call <see cref="WriteEndSet"/>.
    /// </summary>
    /// <remarks>
    /// DynamoDB rejects empty sets and sets with numerically equal elements. Write a set attribute only if it has
    /// elements.
    /// </remarks>
    public void WriteStartNumberSet() => WriteStartSet(NumberSetDescriptor);

    /// <summary>
    /// Starts a binary set, <c>{"BS":[</c>. Write the elements with <see cref="WriteBinarySetElement"/>, then call
    /// <see cref="WriteEndSet"/>.
    /// </summary>
    /// <remarks>DynamoDB rejects empty sets and sets with duplicates. Write a set attribute only if it has elements.</remarks>
    public void WriteStartBinarySet() => WriteStartSet(BinarySetDescriptor);

    /// <summary>Ends a set started with <see cref="WriteStartStringSet"/>, <see cref="WriteStartNumberSet"/> or <see cref="WriteStartBinarySet"/>.</summary>
    public void WriteEndSet()
    {
        Json.WriteEndArray();
        Json.WriteEndObject();
    }

    /// <summary>Writes an element of a string set: a plain JSON string without type descriptor.</summary>
    /// <param name="value">The string.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> holds a lone surrogate, which is not text, and would otherwise be stored as U+FFFD.</exception>
    public void WriteStringSetElement(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfNoText(value, nameof(value));
        Json.WriteStringValue(value);
    }

    /// <summary>Writes an element of a string set from UTF-8 bytes, without a transcoding step: a plain JSON string without type descriptor.</summary>
    /// <param name="utf8Value">The string as UTF-8.</param>
    public void WriteStringSetElement(ReadOnlySpan<byte> utf8Value) => Json.WriteStringValue(utf8Value);

    /// <summary>Writes an element of a number set, formatted with the invariant culture.</summary>
    /// <typeparam name="T">Any .NET number type.</typeparam>
    /// <param name="value">The number.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite; DynamoDB cannot store either.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is <see cref="char"/>, which is text: it belongs in a string set.</exception>
    public void WriteNumberSetElement<T>(T value)
        where T : INumberBase<T>
    {
        if (typeof(T) == typeof(char))
        {
            throw new ArgumentException("A char is text, not a number, so it cannot be an element of a number set.", nameof(value));
        }

        DynamoDbNumber.ThrowIfNotFinite(value);
        WriteNumberText(value);
    }

    /// <summary>Writes an element of a number set from its textual representation, sent unchanged.</summary>
    /// <param name="value">The number as text. DynamoDB validates the format.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public void WriteNumberSetElement(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Json.WriteStringValue(value);
    }

    /// <summary>Writes an element of a binary set, encoded as base64.</summary>
    /// <param name="value">The bytes.</param>
    public void WriteBinarySetElement(ReadOnlySpan<byte> value) => Json.WriteBase64StringValue(value);

    /// <summary>
    /// Starts an item: a plain JSON object of attribute names and values, as used for <c>Item</c> or <c>Key</c>
    /// in requests. Unlike a map, it has no type descriptor. Call <see cref="WriteEndItem"/> at the end.
    /// </summary>
    public void WriteStartItem() => Json.WriteStartObject();

    /// <summary>Ends an item started with <see cref="WriteStartItem"/>.</summary>
    public void WriteEndItem() => Json.WriteEndObject();

    /// <summary>
    /// Creates a writer for the text of <c>ToString</c>, which nothing stores and the debugger shows. A lone surrogate comes out as U+FFFD here and does not throw, because this text
    /// is what someone reads to find out why a request failed.
    /// </summary>
    /// <param name="json">The destination.</param>
    /// <returns>The writer.</returns>
    internal static DynamoDbWriter ForDisplay(Utf8JsonWriter json) => new(json) { IsForDisplay = true };

    /// <summary>Writes a number value, <c>{"N":"..."}</c>, from text that is a number already, such as a number of plain JSON, sent unchanged.</summary>
    /// <param name="utf8Text">The number as UTF-8, which needs no escaping.</param>
    internal void WriteNumber(ReadOnlySpan<byte> utf8Text)
    {
        Json.WriteStartObject();
        Json.WriteString(NumberDescriptor, utf8Text);
        Json.WriteEndObject();
    }

    private static void WritePlain(PooledByteBuffer direct, ReadOnlySpan<byte> start, string value)
    {
        direct.Append(start);
        direct.AppendAscii(value);
        direct.Append("\"}"u8);
    }

    private static bool TryWriteNumberDirect<T>(PooledByteBuffer direct, T value)
        where T : INumberBase<T>
    {
        // Numbers format to digits, sign, point and exponent, none of which JSON escapes.
        Span<byte> text = stackalloc byte[StackNumberLength];
        if (!DynamoDbNumber.TryFormat(value, text, out var written))
        {
            return false;
        }

        direct.Append("{\"N\":\""u8);
        direct.Append(text[..written]);
        direct.Append("\"}"u8);
        return true;
    }

    // Text with a lone surrogate would be stored as U+FFFD, so that is a failure, except in the text of ToString.
    private void ThrowIfNoText(string text, string parameterName)
    {
        if (!IsForDisplay)
        {
            WireJson.ThrowIfLoneSurrogate(text, parameterName);
        }
    }

    private void WriteStartSet(JsonEncodedText descriptor)
    {
        Json.WriteStartObject();
        Json.WriteStartArray(descriptor);
    }

    private void WriteNumberText<T>(T value)
        where T : INumberBase<T>
    {
        Span<byte> buffer = stackalloc byte[StackNumberLength];
        if (DynamoDbNumber.TryFormat(value, buffer, out var written))
        {
            Json.WriteStringValue(buffer[..written]);
        }
        else
        {
            WriteLongNumberText(value);
        }
    }

    private void WriteLongNumberText<T>(T value)
        where T : INumberBase<T>
    {
        // Rare (BigInteger and the like), so one string beats a pooled buffer: nothing shared, no retry loop to size it.
        Json.WriteStringValue(DynamoDbNumber.Format(value));
    }
}
