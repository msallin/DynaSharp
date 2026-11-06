using System.Runtime.InteropServices;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="JsonElement"/> to and from the native DynamoDB value that holds the same JSON: an object is a map (<c>M</c>), an array a list (<c>L</c>), a string <c>S</c>, a number
/// <c>N</c> with its digits as written, <c>true</c> and <c>false</c> <c>BOOL</c>, and null <c>NULL</c>. It is the built-in converter of a property of type <see cref="JsonElement"/>, so a
/// document whose schema belongs to someone else, such as the payload of an event, is stored as a map that filters, projections and updates reach by path.
/// </summary>
/// <remarks>
/// <para>
/// A null in an object is left out of the map, so a field that was null reads back missing. A null in an array is <c>NULL</c>, which keeps the positions of the others. A number keeps its
/// text on the way in and back, but DynamoDB stores numbers in a canonical form, so <c>1.50</c> reads back as <c>1.5</c> and <c>1E+2</c> as <c>100</c>.
/// </para>
/// <para>
/// A write throws <see cref="ArgumentException"/> for what DynamoDB would refuse, naming the place in the JSON such as <c>$.lines[2].weight</c>: a number with more than 38 significant
/// digits or a magnitude outside 1E-130 to below 1E+126, a value nested deeper than 32 levels counted from the attribute, an empty name, a name twice in one object, text that is not valid UTF-8 or UTF-16,
/// and a <see langword="default"/> <see cref="JsonElement"/>, which holds no JSON. A <see cref="Nullable{T}"/> property leaves the attribute out for <see langword="null"/>. The levels are
/// counted from this value: inside a list or a document it stands deeper, and DynamoDB counts that.
/// </para>
/// <para>
/// A read takes any value: a string set and a number set read as an array, and a binary value and a binary set as the base64 text of the bytes, which a write stores as a string. The
/// element owns its memory, so it outlives the read.
/// </para>
/// </remarks>
public readonly struct JsonElementConverter : IDynamoDbConverter<JsonElement>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException">DynamoDB cannot store the JSON, see the remarks, or <paramref name="value"/> is a <see langword="default"/> instance.</exception>
    public static void Write(DynamoDbWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new UnstorableJsonException("A default JsonElement holds no JSON. Leave the attribute out with a JsonElement? that is null.", nameof(value));
        }

        JsonBridge.Write(writer, JsonMarshal.GetRawUtf8Value(value), JsonBridge.ElementOptions, nameof(value));
    }

    /// <inheritdoc/>
    /// <exception cref="FormatException">A number is not in the form of a JSON number, which DynamoDB never sends.</exception>
    public static JsonElement Read(ref DynamoDbReader reader, AttributeType type)
    {
        using var buffer = new PooledByteBuffer(256);
        using (var json = WireJson.CreateWriter(buffer))
        {
            JsonBridge.Read(ref reader, type, json);
        }

        return JsonElement.Parse(buffer.WrittenSpan, JsonBridge.DocumentOptions);
    }
}
