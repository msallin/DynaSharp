using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts between plain JSON and DynamoDB attribute values, for <see cref="AttributeValue.FromJson(ReadOnlySpan{byte})"/>, <see cref="AttributeValue.WriteJson"/> and
/// <see cref="JsonElementConverter"/>. Both directions go through the wire format: JSON in is written with a <see cref="DynamoDbWriter"/>, and JSON out is read with a
/// <see cref="DynamoDbReader"/>, so this holds only what JSON and DynamoDB disagree on.
/// </summary>
/// <remarks>
/// <code>
/// JSON                      DynamoDB
/// {"a":1,"b":null}          {"M":{"a":{"N":"1"}}}           a null in an object is left out
/// [1,null]                  {"L":[{"N":"1"},{"NULL":true}]} a null in an array is NULL, which keeps the positions
/// null                      {"NULL":true}
/// "x", true, 1.50           {"S":"x"}, {"BOOL":true}, {"N":"1.50"}, the digits as written
/// ["a"], ["1"], ["AQ=="]    {"SS":["a"]}, {"NS":["1"]}, {"BS":["AQ=="]}, one way only: a set reads as an array
/// "AQ=="                    {"B":"AQ=="}, one way only: a binary reads as its base64 text
/// </code>
/// </remarks>
internal static class JsonBridge
{
    /// <summary>How many levels a value may have in DynamoDB, counting the value at the bottom: a string is one, an object of a string two.</summary>
    internal const int MaxLevels = 32;

    /// <summary>The settings for the text of a <see cref="JsonElement"/>, which its document may have parsed with comments or trailing commas.</summary>
    internal static readonly JsonReaderOptions ElementOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The settings for a <see cref="JsonElement"/> made from a value, which has the levels of DynamoDB, or of a value built by hand that a write would refuse.</summary>
    internal static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = WireJson.ReaderOptions.MaxDepth };

    /// <summary>Writes one JSON value as one attribute value.</summary>
    /// <param name="writer">The writer, positioned where a value is expected.</param>
    /// <param name="utf8Json">The JSON.</param>
    /// <param name="options">The settings of the JSON reader.</param>
    /// <param name="paramName">The parameter that holds the JSON, for the exception.</param>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="UnstorableJsonException">DynamoDB cannot store the JSON.</exception>
    internal static void Write(DynamoDbWriter writer, ReadOnlySpan<byte> utf8Json, JsonReaderOptions options, string paramName)
    {
        Span<JsonValueWriter.Frame> frames = stackalloc JsonValueWriter.Frame[MaxLevels];
        var values = new JsonValueWriter(writer, utf8Json, options, frames, paramName);
        try
        {
            values.WriteDocument();
        }
        finally
        {
            values.Dispose();
        }
    }

    /// <summary>Writes the attribute value the reader stands on as plain JSON, and reads it to its end, as a converter does.</summary>
    /// <param name="reader">The reader, on the value of the type <paramref name="type"/>.</param>
    /// <param name="type">The type of the value.</param>
    /// <param name="json">The destination.</param>
    /// <exception cref="FormatException">A number is no JSON number, which only a value built by hand can hold.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is <see cref="AttributeType.None"/>.</exception>
    internal static void Read(ref DynamoDbReader reader, AttributeType type, Utf8JsonWriter json)
    {
        switch (type)
        {
            case AttributeType.String or AttributeType.Binary:
                json.WriteStringValue(reader.GetStringUtf8());
                break;
            case AttributeType.Number:
                WriteNumber(json, reader.GetStringUtf8());
                break;
            case AttributeType.Boolean:
                json.WriteBooleanValue(reader.GetBool());
                break;
            case AttributeType.Null:
                json.WriteNullValue();
                break;
            case AttributeType.List:
                json.WriteStartArray();
                while (reader.TryReadStartAttribute(out var element))
                {
                    Read(ref reader, element, json);
                    reader.ReadEndAttribute();
                }

                json.WriteEndArray();
                break;
            case AttributeType.Map:
                json.WriteStartObject();
                while (reader.TryReadAttributeName())
                {
                    json.WritePropertyName(reader.GetAttributeNameUtf8());
                    Read(ref reader, reader.ReadStartAttribute(), json);
                    reader.ReadEndAttribute();
                }

                json.WriteEndObject();
                break;
            case AttributeType.StringSet or AttributeType.BinarySet:
                json.WriteStartArray();
                while (reader.TryReadSetElement())
                {
                    json.WriteStringValue(reader.GetStringUtf8());
                }

                json.WriteEndArray();
                break;
            case AttributeType.NumberSet:
                json.WriteStartArray();
                while (reader.TryReadSetElement())
                {
                    WriteNumber(json, reader.GetStringUtf8());
                }

                json.WriteEndArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Not a DynamoDB attribute type.");
        }
    }

    // The text as it is, so no digit is lost to a double. Unchecked, it could be any JSON, which a number made by hand with FromNumber could hold.
    private static void WriteNumber(Utf8JsonWriter json, ReadOnlySpan<byte> text)
    {
        if (!DynamoDbNumber.IsJsonNumber(text))
        {
            throw new FormatException("A number is not in the form that JSON has for a number, such as -1.5E+3.");
        }

        // A raw value gets no indentation, so an indented writer takes the number through an element, which writes the same text where indentation puts it.
        if (json.Options.Indented)
        {
            JsonElement.Parse(text).WriteTo(json);
        }
        else
        {
            json.WriteRawValue(text, skipInputValidation: true);
        }
    }
}
