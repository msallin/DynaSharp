using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Checks the JSON of a map of attribute values, an item, a key or <c>ExpressionAttributeValues</c>, before it is read: an attribute without a name, a set without elements, a <c>NULL</c> that is not
/// <c>true</c>, and a value nested past the 32 levels DynamoDB stores. It reads the JSON as it is, so that a request nested far deeper than the reader of the client takes is still answered with
/// the nesting error.
/// </summary>
/// <remarks>
/// <para>
/// A value counts one level, and each map or list around it one more: 31 maps around a string are 32 levels, the deepest DynamoDB takes. What DynamoDB says of a value past it depends on where it stands and how
/// deep it is. The single-item operations word an item or a value of <c>ExpressionAttributeValues</c> of up to 63 levels inside the validation envelope and an item deeper than that bare; they name the key of
/// a value of <c>ExpressionAttributeValues</c> deeper than that. A batch and a transaction word an item bare, and a value of <c>ExpressionAttributeValues</c> bare up to 60 levels and naming its key deeper than that (Parity <c>tier3/limits/nestingDepth</c> and <c>tier3/validation-ordering/deepRequests</c>).
/// </para>
/// <para>
/// A value of another shape than DynamoDB JSON is left to the reader that follows.
/// </para>
/// </remarks>
internal static class ItemShape
{
    // Past it a value of ExpressionAttributeValues in a batch or a transaction names its key.
    private const int ElementNamedLevels = 60;

    // Past it the single-item operations read the request in another layer, and word the nesting error as that layer does.
    private const int OlderLayerLevels = 63;

    private const string Invalid = "One or more parameter values were invalid: ";

    private const string NestingMessage = "Nesting Levels have exceeded supported limits: Attributes in the item have nested levels beyond supported limit";

    /// <summary>Checks a map of attribute values.</summary>
    /// <param name="map">The map in DynamoDB JSON. A value that is no object is left to the reader.</param>
    /// <param name="member">The member it is: <c>Item</c>, <c>Key</c>, <c>Keys</c> or <c>ExpressionAttributeValues</c>.</param>
    /// <param name="single"><see langword="true"/> for a request of its own, <see langword="false"/> for an element of a batch or a transaction.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static void Check(JsonElement map, string member, bool single)
    {
        if (map.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var attribute in map.EnumerateObject())
        {
            ThrowIfNameless(attribute.Name);
        }

        foreach (var attribute in map.EnumerateObject())
        {
            var levels = Walk(attribute.Value);
            if (levels > AttributeRules.MaxNesting)
            {
                throw NestingError(member, attribute.Name, levels, single);
            }
        }
    }

    private static int Walk(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return 1;
        }

        var members = value.EnumerateObject().ToList();
        if (members.Count != 1)
        {
            return 1;
        }

        var (type, content) = (members[0].Name, members[0].Value);
        switch (type)
        {
            case "M" when content.ValueKind == JsonValueKind.Object:
                var deepestMember = 0;
                foreach (var child in content.EnumerateObject())
                {
                    ThrowIfNameless(child.Name);
                    deepestMember = Math.Max(deepestMember, Walk(child.Value));
                }

                return 1 + deepestMember;
            case "L" when content.ValueKind == JsonValueKind.Array:
                var deepestElement = 0;
                foreach (var element in content.EnumerateArray())
                {
                    deepestElement = Math.Max(deepestElement, Walk(element));
                }

                return 1 + deepestElement;
            case "SS" or "NS" or "BS" when content.ValueKind == JsonValueKind.Array && content.GetArrayLength() == 0:
                throw FakeError.Validation(Invalid + type switch
                {
                    "SS" => "An string set  may not be empty",
                    "NS" => "An number set  may not be empty",
                    _ => "Binary sets should not be empty",
                });
            case "NULL" when content.ValueKind == JsonValueKind.False:
                throw FakeError.Validation(Invalid + "Null attribute value types must have the value of true");
            default:
                return 1;
        }
    }

    private static void ThrowIfNameless(string name)
    {
        if (name.Length == 0)
        {
            throw FakeError.Validation("Empty attribute name");
        }
    }

    private static FakeError NestingError(string member, string name, int levels, bool single)
    {
        if (member == "ExpressionAttributeValues")
        {
            return levels > (single ? OlderLayerLevels : ElementNamedLevels)
                ? FakeError.Validation($"ExpressionAttributeValues contains invalid value: {NestingMessage} for key {name}")
                : single ? FakeError.Enveloped(NestingMessage) : FakeError.Validation(NestingMessage);
        }

        return single && member == "Item" && levels <= OlderLayerLevels ? FakeError.Enveloped(NestingMessage) : FakeError.Validation(NestingMessage);
    }
}
