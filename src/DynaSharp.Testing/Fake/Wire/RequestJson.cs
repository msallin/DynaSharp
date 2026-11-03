using System.Text;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Reads the members of a request body. The rule of the fake is that no member is ignored: <see cref="Members"/> names every member an object may have, and anything else is
/// <c>UnsupportedByFakeException</c>, so a legacy parameter, a member of a newer API or a typo fails the request instead of changing nothing. A value DynamoDB rejects, such as an enum value it does not have, is the <c>ValidationException</c> DynamoDB answers.
/// </summary>
/// <remarks>
/// <para>
/// A member of the wrong JSON kind, a string where a number goes, is a <c>SerializationException</c>, as DynamoDB answers it. A JSON <c>null</c> counts as a member that is not there,
/// which is how DynamoDB reads it. A required member that is missing is a <c>ValidationException</c> that names it in camel case, as DynamoDB does: <c>Value null at 'tableName'</c>.
/// </para>
/// <para>
/// Items and attribute values go through the reader of the client and then through <see cref="AttributeRules.Normalize(IReadOnlyDictionary{string, AttributeValue})"/>, so a value the
/// client could not read, or one DynamoDB rejects, is a <c>ValidationException</c> before the request does anything.
/// </para>
/// </remarks>
internal static class RequestJson
{
    /// <summary>Checks that an object has no member but those named.</summary>
    /// <param name="value">The object: a request or a part of one.</param>
    /// <param name="where">What it is, for the message: the operation, or <c>KeySchema of CreateTable</c>.</param>
    /// <param name="allowed">The members it may have.</param>
    /// <exception cref="FakeError">A <c>SerializationException</c> if it is no object, an <c>UnsupportedByFakeException</c> for another member.</exception>
    public static void Members(JsonElement value, string where, params ReadOnlySpan<string> allowed)
    {
        ThrowIfNotKind(value, JsonValueKind.Object, where, "an object");
        foreach (var member in value.EnumerateObject())
        {
            if (!allowed.Contains(member.Name))
            {
                throw FakeError.Unsupported($"the member {member.Name}", where);
            }
        }
    }

    /// <summary>Gets a member that may be left out.</summary>
    /// <param name="value">The object.</param>
    /// <param name="member">The member.</param>
    /// <returns>The value, or <see langword="null"/> when the member is not there or is JSON <c>null</c>.</returns>
    public static JsonElement? Optional(JsonElement value, string member) =>
        value.TryGetProperty(member, out var found) && found.ValueKind != JsonValueKind.Null ? found : null;

    /// <summary>Gets a member that must be there.</summary>
    /// <param name="value">The object.</param>
    /// <param name="member">The member.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static JsonElement Required(JsonElement value, string member) =>
        Optional(value, member) ?? throw FakeError.Validation($"1 validation error detected: Value null at '{CamelCase(member)}' failed to satisfy constraint: Member must not be null");

    /// <summary>Reads a string.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>The string.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static string ReadString(JsonElement value, string member)
    {
        ThrowIfNotKind(value, JsonValueKind.String, member, "a string");
        return value.GetString()!;
    }

    /// <summary>Reads a number that fits an <see cref="int"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>The number.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static int ReadInt(JsonElement value, string member)
    {
        ThrowIfNotKind(value, JsonValueKind.Number, member, "a number");
        return value.TryGetInt32(out var number) ? number : throw FakeError.Serialization($"The member {member} is not an integer of 32 bits: {value.GetRawText()}.");
    }

    /// <summary>Reads a number that fits a <see cref="long"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>The number.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static long ReadLong(JsonElement value, string member)
    {
        ThrowIfNotKind(value, JsonValueKind.Number, member, "a number");
        return value.TryGetInt64(out var number) ? number : throw FakeError.Serialization($"The member {member} is not an integer of 64 bits: {value.GetRawText()}.");
    }

    /// <summary>Reads a boolean.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>The boolean.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static bool ReadBool(JsonElement value, string member) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw NotKind(value, member, "a boolean"),
    };

    /// <summary>Reads the wire name of an enum through the one table of its spellings.</summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <param name="names">The spellings, from <see cref="WireNames"/>.</param>
    /// <param name="noneIsDefault"><see langword="true"/> for an enum whose zero member is <c>None</c>, which is <c>NONE</c> on the wire and has no entry in the table.</param>
    /// <returns>The member.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c> for no string, a <c>ValidationException</c> for a name the enum does not have, which DynamoDB rejects as well.</exception>
    public static TEnum ReadEnum<TEnum>(JsonElement value, string member, EnumWireNames<TEnum> names, bool noneIsDefault = false)
        where TEnum : struct, Enum
    {
        var name = ReadString(value, member);
        if (noneIsDefault && name == "NONE")
        {
            return default;
        }

        if (names.TryGetValue(name, out var parsed))
        {
            return parsed;
        }

        // 1 validation error detected: Value 'BOOL' at 'attributeType' failed to satisfy constraint: Member must satisfy enum value set: [S, N, B]
        IEnumerable<string> spellings = noneIsDefault ? ["NONE"] : [];
        foreach (var known in Enum.GetValues<TEnum>())
        {
            if (names.TryGetName(known, out var spelling))
            {
                spellings = spellings.Append(spelling);
            }
        }

        throw FakeError.Validation($"1 validation error detected: Value '{name}' at '{CamelCase(member)}' failed to satisfy constraint: Member must satisfy enum value set: [{string.Join(", ", spellings)}]");
    }

    /// <summary>Reads an item, a key or a map of attribute values, such as <c>ExpressionAttributeValues</c>, and checks every value.</summary>
    /// <param name="value">The value: an object of attribute names and values in DynamoDB JSON.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>A new item, in its stored form.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c> for no object, a <c>ValidationException</c> for a value that is malformed or that DynamoDB rejects.</exception>
    public static Item ReadItem(JsonElement value, string member)
    {
        ThrowIfNotKind(value, JsonValueKind.Object, member, "an object");
        Item item;
        try
        {
            var reader = new DynamoDbReader(Encoding.UTF8.GetBytes(value.GetRawText()));
            item = AttributeValueConverter.ReadItem(ref reader);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw FakeError.Validation($"Supplied AttributeValue is not valid in {member}: {exception.Message}");
        }

        return AttributeRules.Normalize(item);
    }

    /// <summary>Reads an object of strings, such as <c>ExpressionAttributeNames</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>A new map.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static Dictionary<string, string> ReadStringMap(JsonElement value, string member)
    {
        ThrowIfNotKind(value, JsonValueKind.Object, member, "an object");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in value.EnumerateObject())
        {
            map[entry.Name] = ReadString(entry.Value, member);
        }

        return map;
    }

    /// <summary>Reads the elements of an array.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>The elements.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static IReadOnlyList<JsonElement> ReadArray(JsonElement value, string member)
    {
        ThrowIfNotKind(value, JsonValueKind.Array, member, "an array");
        return [.. value.EnumerateArray()];
    }

    /// <summary>Reads the members of an object whose member names are data, such as the table names of <c>RequestItems</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>The members, JSON <c>null</c> values left out.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static IReadOnlyList<JsonProperty> ReadObject(JsonElement value, string member)
    {
        ThrowIfNotKind(value, JsonValueKind.Object, member, "an object");
        return [.. value.EnumerateObject().Where(static entry => entry.Value.ValueKind != JsonValueKind.Null)];
    }

    /// <summary>Reads an array of strings.</summary>
    /// <param name="value">The value.</param>
    /// <param name="member">The member, for the message.</param>
    /// <returns>The strings.</returns>
    /// <exception cref="FakeError">A <c>SerializationException</c>.</exception>
    public static IReadOnlyList<string> ReadStringList(JsonElement value, string member) => [.. ReadArray(value, member).Select(element => ReadString(element, member))];

    private static string CamelCase(string member) => char.ToLowerInvariant(member[0]) + member[1..];

    private static void ThrowIfNotKind(JsonElement value, JsonValueKind kind, string member, string expected)
    {
        if (value.ValueKind != kind)
        {
            throw NotKind(value, member, expected);
        }
    }

    private static FakeError NotKind(JsonElement value, string member, string expected) =>
        FakeError.Serialization($"The member {member} must be {expected}, and it is {value.ValueKind}.");
}
