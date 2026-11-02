using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// Reads DynamoDB JSON: attribute values in their typed form, such as <c>{"S":"abc"}</c>, and items, which are JSON
/// objects that map attribute names to attribute values.
/// </summary>
/// <remarks>
/// <para>
/// This is the only code that parses DynamoDB JSON. <see cref="AttributeValue"/>, entities mapped by the source
/// generator and custom converters (<see cref="IDynamoDbConverter{T}"/>) all read through it.
/// </para>
/// <para>
/// The reader is a cursor over the JSON tokens. Every <c>Read*</c> and <c>TryRead*</c> method first advances, then
/// checks the token it arrived at; the <c>Get*</c> methods return the value at the current position without moving.
/// An attribute value is read with <see cref="ReadStartAttribute"/> (consumes <c>{</c> and the type descriptor and
/// stops on the value), a getter or the elements of a list, map or set, and <see cref="ReadEndAttribute"/> (consumes
/// <c>}</c>):
/// </para>
/// <code>
/// reader.ReadStartItem();
/// while (reader.TryReadAttributeName())
/// {
///     if (reader.AttributeNameEquals("pk"u8))
///     {
///         var type = reader.ReadStartAttribute();   // positioned on the value
///         pk = reader.GetString();                   // after checking type == AttributeType.String
///         reader.ReadEndAttribute();
///     }
///     else
///     {
///         reader.SkipAttributeValue();
///     }
/// }
/// </code>
/// <para>
/// JSON that does not have the shape of DynamoDB JSON, for example an unknown type descriptor or a value of the
/// wrong JSON kind, throws <see cref="JsonException"/>. Escaped JSON text (<c>\u0041</c>) is handled everywhere.
/// The getters do not know the attribute type: checking it is up to the caller.
/// </para>
/// <para>DynaSharp passes instances to converters by reference.</para>
/// </remarks>
public ref struct DynamoDbReader
{
    // Type descriptors have at most four characters, each at most six bytes when escaped as \uXXXX.
    private const int MaxEscapedDescriptorLength = 24;

    private Utf8JsonReader _json;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbReader"/> struct over complete JSON. DynaSharp creates readers for its
    /// own responses; use this to test a custom converter.
    /// </summary>
    /// <param name="utf8Json">The JSON as UTF-8. It must stay valid while the reader is used.</param>
    public DynamoDbReader(ReadOnlySpan<byte> utf8Json)
    {
        _json = new Utf8JsonReader(utf8Json, WireJson.ReaderOptions);
    }

    /// <summary>Gets the underlying JSON reader, for response envelopes around the attribute values.</summary>
    [UnscopedRef]
    internal ref Utf8JsonReader Json => ref _json;

    /// <summary>Reads the start of an item (<c>{</c>), a plain JSON object of attribute names and values.</summary>
    /// <remarks>Then read the attributes with <see cref="TryReadAttributeName"/>, which returns <see langword="false"/> at the end of the item.</remarks>
    /// <exception cref="JsonException">The next token is not the start of an object.</exception>
    public void ReadStartItem()
    {
        Advance();
        if (_json.TokenType != JsonTokenType.StartObject)
        {
            throw Unexpected("the start of an item ('{')");
        }
    }

    /// <summary>Reads the start of the next item (<c>{</c>) in an array of items, such as <c>Items</c> in a query response, or the end of the array.</summary>
    /// <returns>
    /// <see langword="true"/> when positioned after the <c>{</c> of an item; read its attributes with
    /// <see cref="TryReadAttributeName"/>, which consumes the closing <c>}</c>. <see langword="false"/> at the end of the array
    /// (<c>]</c>), which is consumed.
    /// </returns>
    /// <exception cref="JsonException">The next token is neither the start of an item nor the end of an array.</exception>
    public bool TryReadStartItem()
    {
        Advance();
        return _json.TokenType switch
        {
            JsonTokenType.StartObject => true,
            JsonTokenType.EndArray => false,
            _ => throw Unexpected("the start of an item ('{') or the end of the array (']')"),
        };
    }

    /// <summary>Reads the next attribute name in an item or map, or the end of the item or map.</summary>
    /// <returns>
    /// <see langword="true"/> when positioned on an attribute name; read its value next, or skip it with
    /// <see cref="SkipAttributeValue"/>. <see langword="false"/> at the end of the item or map (<c>}</c>), which is consumed.
    /// </returns>
    /// <exception cref="JsonException">The next token is neither an attribute name nor the end of an object.</exception>
    public bool TryReadAttributeName()
    {
        Advance();
        return _json.TokenType switch
        {
            JsonTokenType.PropertyName => true,
            JsonTokenType.EndObject => false,
            _ => throw Unexpected("an attribute name or the end of an item or map ('}')"),
        };
    }

    /// <summary>Compares the attribute name the reader is positioned on with a UTF-8 name, ordinally.</summary>
    /// <param name="utf8Name">The name to compare with, typically a literal such as <c>"pk"u8</c>.</param>
    /// <returns><see langword="true"/> if the names are equal; escaped names compare by their unescaped text.</returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on an attribute name.</exception>
    public readonly bool AttributeNameEquals(ReadOnlySpan<byte> utf8Name) => _json.ValueTextEquals(utf8Name);

    /// <summary>Gets the attribute name the reader is positioned on.</summary>
    /// <returns>The unescaped name.</returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on an attribute name.</exception>
    public readonly string GetAttributeName() => _json.GetString()!;

    /// <summary>Gets the attribute name the reader is positioned on as UTF-8, to compare it with known names without creating a string.</summary>
    /// <returns>
    /// The unescaped name. For a name without escape sequences, which is the normal case, this is a view of the input and allocates nothing; it is
    /// valid until the reader moves on. A name with escapes (<c>A</c>) is unescaped into a new array.
    /// </returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on an attribute name.</exception>
    public readonly ReadOnlySpan<byte> GetAttributeNameUtf8()
    {
        if (_json.TokenType != JsonTokenType.PropertyName)
        {
            throw new InvalidOperationException($"Cannot read an attribute name from the JSON token {_json.TokenType}.");
        }

        return ValueUtf8();
    }

    /// <summary>
    /// Reads the start of an attribute value (<c>{</c>) and its type descriptor, and stops on the value.
    /// </summary>
    /// <returns>The type of the value. Read the value accordingly, then call <see cref="ReadEndAttribute"/>.</returns>
    /// <exception cref="JsonException">
    /// The next token is not the start of an object, the type descriptor is unknown, or the value does not match the
    /// type (for example a list that is not a JSON array, or <c>{"NULL":false}</c>).
    /// </exception>
    public AttributeType ReadStartAttribute()
    {
        Advance();
        if (_json.TokenType != JsonTokenType.StartObject)
        {
            throw Unexpected("the start of an attribute value ('{')");
        }

        return ReadDescriptorAndValue();
    }

    /// <summary>
    /// Reads the start of the next list element and its type descriptor, and stops on the value; or reads the end of
    /// the list.
    /// </summary>
    /// <param name="type">The type of the element, or <see cref="AttributeType.None"/> at the end of the list.</param>
    /// <returns>
    /// <see langword="true"/> when positioned on an element value; read it, then call <see cref="ReadEndAttribute"/>.
    /// <see langword="false"/> at the end of the list (<c>]</c>), which is consumed; then call
    /// <see cref="ReadEndAttribute"/> for the list itself.
    /// </returns>
    /// <exception cref="JsonException">The next token is neither an attribute value nor the end of an array, or the element is malformed.</exception>
    public bool TryReadStartAttribute(out AttributeType type)
    {
        Advance();
        if (_json.TokenType == JsonTokenType.EndArray)
        {
            type = AttributeType.None;
            return false;
        }

        if (_json.TokenType != JsonTokenType.StartObject)
        {
            throw Unexpected("a list element ('{') or the end of the list (']')");
        }

        type = ReadDescriptorAndValue();
        return true;
    }

    /// <summary>Reads a complete attribute value of a known converter: the start, the value and the end.</summary>
    /// <typeparam name="T">The .NET type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter that reads the value, called directly through the type parameter.</typeparam>
    /// <returns>The value.</returns>
    /// <remarks>
    /// Call it after <see cref="TryReadAttributeName"/> returned <see langword="true"/> and the name was recognized. The value is
    /// <c>{"NULL":true}</c> for a <see langword="null"/> that <typeparamref name="TConverter"/> accepts, see <see cref="NullableConverter{T, TConverter}"/>.
    /// </remarks>
    /// <exception cref="JsonException">The attribute value is malformed, or <typeparamref name="TConverter"/> does not support its type.</exception>
    public T ReadAttribute<T, TConverter>()
        where TConverter : IDynamoDbConverter<T>
    {
        var type = ReadStartAttribute();
        var value = TConverter.Read(ref this, type);
        ReadEndAttribute();
        return value;
    }

    /// <summary>Reads the end of an attribute value (<c>}</c>).</summary>
    /// <exception cref="JsonException">The next token is not the end of an object.</exception>
    public void ReadEndAttribute()
    {
        Advance();
        if (_json.TokenType != JsonTokenType.EndObject)
        {
            throw Unexpected("the end of the attribute value ('}')");
        }
    }

    /// <summary>Reads the next element of a string, number or binary set, or the end of the set.</summary>
    /// <returns>
    /// <see langword="true"/> when positioned on an element; get it with <see cref="GetString"/>,
    /// <see cref="GetNumber{T}"/>, <see cref="GetNumberText"/> or <see cref="GetBinary"/>. <see langword="false"/> at the
    /// end of the set (<c>]</c>), which is consumed; then call <see cref="ReadEndAttribute"/> for the set itself.
    /// </returns>
    /// <exception cref="JsonException">The next token is neither a JSON string nor the end of an array.</exception>
    public bool TryReadSetElement()
    {
        Advance();
        return _json.TokenType switch
        {
            JsonTokenType.String => true,
            JsonTokenType.EndArray => false,
            _ => throw Unexpected("a set element (a JSON string) or the end of the set (']')"),
        };
    }

    /// <summary>Gets the string of a string value (<c>S</c>) or string set element.</summary>
    /// <returns>The unescaped string.</returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a JSON string.</exception>
    public readonly string GetString() => _json.GetString()!;

    /// <summary>Gets the string of a string value (<c>S</c>) or string set element as UTF-8, to parse or compare it without creating a string.</summary>
    /// <returns>
    /// The unescaped text. For a string without escape sequences, which is the normal case, this is a view of the input and allocates nothing; it
    /// is valid until the reader moves on. A string with escapes is unescaped into a new array.
    /// </returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a JSON string.</exception>
    public readonly ReadOnlySpan<byte> GetStringUtf8()
    {
        if (_json.TokenType != JsonTokenType.String)
        {
            throw new InvalidOperationException($"Cannot read a string from the JSON token {_json.TokenType}.");
        }

        return ValueUtf8();
    }

    /// <summary>Parses a number value (<c>N</c>) or number set element with the invariant culture.</summary>
    /// <typeparam name="T">The target number type.</typeparam>
    /// <returns>The number. Exponent notation such as <c>1E+2</c> is accepted, also for integer types.</returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a JSON string.</exception>
    /// <exception cref="FormatException">The text is not a number.</exception>
    /// <exception cref="OverflowException">
    /// The number is outside the range of <typeparamref name="T"/>, has a fractional part and <typeparamref name="T"/> is an integer type, or <typeparamref name="T"/> is
    /// <see cref="decimal"/> and cannot hold its exact value, which takes 28 to 29 significant digits at most, where DynamoDB has 38. A binary floating-point type counts a number
    /// that rounds to infinity, and <c>NaN</c>, as out of range; it rounds any other number to the nearest value it holds, and one below its range reads as 0.
    /// </exception>
    public readonly T GetNumber<T>()
        where T : INumberBase<T>
    {
        if (_json.TokenType != JsonTokenType.String)
        {
            throw new InvalidOperationException($"Cannot read a number from the JSON token {_json.TokenType}.");
        }

        return _json.ValueIsEscaped ? DynamoDbNumber.Parse<T>(_json.GetString()) : DynamoDbNumber.Parse<T>(_json.ValueSpan);
    }

    /// <summary>Gets the text of a number value (<c>N</c>) or number set element, exactly as transferred.</summary>
    /// <returns>The number as text. Use it for numbers beyond the range or precision of .NET types (up to 38 digits).</returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a JSON string.</exception>
    public readonly string GetNumberText() => _json.GetString()!;

    /// <summary>Decodes a binary value (<c>B</c>) or binary set element from base64.</summary>
    /// <returns>The bytes, a new array.</returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a JSON string.</exception>
    /// <exception cref="JsonException">The text is not valid base64.</exception>
    public readonly byte[] GetBinary() =>
        _json.TryGetBytesFromBase64(out var bytes) ? bytes : throw Invalid("The binary value is not valid base64.");

    /// <summary>Gets the boolean of a boolean value (<c>BOOL</c>).</summary>
    /// <returns>The boolean.</returns>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a JSON boolean.</exception>
    public readonly bool GetBool() => _json.GetBoolean();

    /// <summary>
    /// Skips the next attribute value as a whole, including its braces: the value of the attribute name the reader is
    /// positioned on, or the next list element. Use it for attributes the caller does not know.
    /// </summary>
    /// <exception cref="JsonException">The next token is not the start of an object.</exception>
    public void SkipAttributeValue()
    {
        Advance();
        if (_json.TokenType != JsonTokenType.StartObject)
        {
            throw Unexpected("the start of an attribute value ('{')");
        }

        _json.Skip();
    }

    /// <summary>Tries to parse a number value (<c>N</c>) or number set element with the invariant culture, without throwing for text that does not fit.</summary>
    /// <typeparam name="T">The target number type.</typeparam>
    /// <param name="value">The number, or the default value when parsing failed.</param>
    /// <returns><see langword="true"/> if the text is a number that <typeparamref name="T"/> can hold.</returns>
    /// <remarks>Unlike <see cref="GetNumber{T}"/>, a failure carries no exception, so no message can quote the text.</remarks>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a JSON string.</exception>
    internal readonly bool TryGetNumber<T>([MaybeNullWhen(false)] out T value)
        where T : INumberBase<T>
    {
        if (_json.TokenType != JsonTokenType.String)
        {
            throw new InvalidOperationException($"Cannot read a number from the JSON token {_json.TokenType}.");
        }

        return _json.ValueIsEscaped ? DynamoDbNumber.TryParse(_json.GetString(), out value) : DynamoDbNumber.TryParse(_json.ValueSpan, out value);
    }

    /// <summary>Moves to the next JSON token. Truncated input ends in a <see cref="JsonException"/>, not in a token that looks wrong.</summary>
    internal void Advance()
    {
        if (!_json.Read())
        {
            throw Invalid("Unexpected end of the DynamoDB JSON data.");
        }
    }

    private static AttributeType ToAttributeType(ReadOnlySpan<byte> descriptor) => descriptor.Length switch
    {
        1 => descriptor[0] switch
        {
            (byte)'S' => AttributeType.String,
            (byte)'N' => AttributeType.Number,
            (byte)'B' => AttributeType.Binary,
            (byte)'L' => AttributeType.List,
            (byte)'M' => AttributeType.Map,
            _ => AttributeType.None,
        },
        2 when descriptor[1] == (byte)'S' => descriptor[0] switch
        {
            (byte)'S' => AttributeType.StringSet,
            (byte)'N' => AttributeType.NumberSet,
            (byte)'B' => AttributeType.BinarySet,
            _ => AttributeType.None,
        },
        4 when descriptor.SequenceEqual("BOOL"u8) => AttributeType.Boolean,
        4 when descriptor.SequenceEqual("NULL"u8) => AttributeType.Null,
        _ => AttributeType.None,
    };

    private static bool IsValueToken(AttributeType type, JsonTokenType token) => type switch
    {
        AttributeType.Boolean => token is JsonTokenType.True or JsonTokenType.False,
        AttributeType.Null => token == JsonTokenType.True,
        AttributeType.List or AttributeType.StringSet or AttributeType.NumberSet or AttributeType.BinarySet
            => token == JsonTokenType.StartArray,
        AttributeType.Map => token == JsonTokenType.StartObject,
        _ => token == JsonTokenType.String,
    };

    private static JsonException Invalid(string message) => new(message);

    // Escaped names and strings are rare, so one array beats a scratch buffer that every caller would have to provide.
    private readonly ReadOnlySpan<byte> ValueUtf8() => _json.ValueIsEscaped ? Unescape() : _json.ValueSpan;

    private readonly ReadOnlySpan<byte> Unescape()
    {
        var bytes = new byte[_json.ValueSpan.Length];
        return bytes.AsSpan(0, _json.CopyString(bytes));
    }

    private AttributeType ReadDescriptorAndValue()
    {
        // Inside an object, Utf8JsonReader only returns a property name or the end of the object.
        Advance();
        if (_json.TokenType != JsonTokenType.PropertyName)
        {
            throw Unexpected("a type descriptor such as \"S\"");
        }

        var type = _json.ValueIsEscaped ? ParseEscapedDescriptor() : ToAttributeType(_json.ValueSpan);
        if (type == AttributeType.None)
        {
            throw Invalid($"Unknown DynamoDB type descriptor \"{_json.GetString()}\" at byte {_json.TokenStartIndex}.");
        }

        Advance();
        if (!IsValueToken(type, _json.TokenType))
        {
            throw Invalid($"The JSON token {_json.TokenType} at byte {_json.TokenStartIndex} is not a valid value for a DynamoDB attribute of type {type}.");
        }

        return type;
    }

    private readonly AttributeType ParseEscapedDescriptor()
    {
        if (_json.ValueSpan.Length > MaxEscapedDescriptorLength)
        {
            return AttributeType.None;
        }

        Span<byte> unescaped = stackalloc byte[MaxEscapedDescriptorLength];
        return ToAttributeType(unescaped[.._json.CopyString(unescaped)]);
    }

    private readonly JsonException Unexpected(string expected) =>
        Invalid($"Expected {expected} but found the JSON token {_json.TokenType} at byte {_json.TokenStartIndex}.");
}
