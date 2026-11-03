namespace DynaSharp.Testing.Fake;

/// <summary>
/// What DynamoDB checks in every attribute value of a request, and the form the fake stores: numbers in their canonical text, sets without duplicates, documents nested at most 32 levels.
/// </summary>
/// <remarks>
/// <para>
/// A number goes through <see cref="DecimalNumber"/> and comes back in its canonical text, <c>{"N":"01.50"}</c> as <c>{"N":"1.5"}</c>, which is what DynamoDB answers with. So two numbers that are
/// equal are equal as text too, and a key <c>1.0</c> finds the item of the key <c>1</c>.
/// </para>
/// <para>
/// A set with an element twice is rejected, numbers by value: <c>["1","1.0"]</c> is a duplicate. An empty set does not get this far: the reader of the client rejects it, and so does
/// <see cref="AttributeValue"/>.
/// </para>
/// </remarks>
internal static class AttributeRules
{
    /// <summary>How many levels a value of an item may have in DynamoDB, counting the value at the bottom: an attribute is level 1, a value in a map or a list of it level 2, so 31 maps around a string are the deepest.</summary>
    public const int MaxNesting = 32;

    /// <summary>Checks every value of an item and gives a copy in the stored form.</summary>
    /// <param name="item">The item, or a key, or the values of <c>ExpressionAttributeValues</c>.</param>
    /// <returns>A new item with the values in their stored form.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a value DynamoDB rejects.</exception>
    public static Item Normalize(IReadOnlyDictionary<string, AttributeValue> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var copy = new Item(item.Count);
        foreach (var (name, value) in item)
        {
            copy.Add(name, Normalize(value, 0));
        }

        return copy;
    }

    /// <summary>Checks one value and gives it in its stored form.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value in its stored form.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a value DynamoDB rejects.</exception>
    public static AttributeValue Normalize(AttributeValue value) => Normalize(value, 0);

    /// <summary>Gets the type descriptor of DynamoDB JSON, the name a message uses for a type: <c>S</c>, <c>N</c>, <c>BOOL</c>, <c>SS</c>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The descriptor, or <c>NONE</c> for a default value, which has no type.</returns>
    public static string Descriptor(AttributeType type) => type switch
    {
        AttributeType.String => "S",
        AttributeType.Number => "N",
        AttributeType.Binary => "B",
        AttributeType.Boolean => "BOOL",
        AttributeType.Null => "NULL",
        AttributeType.List => "L",
        AttributeType.Map => "M",
        AttributeType.StringSet => "SS",
        AttributeType.NumberSet => "NS",
        AttributeType.BinarySet => "BS",
        _ => "NONE",
    };

    // depth is 0 for an attribute of the item, so it is the level less one.
    private static AttributeValue Normalize(AttributeValue value, int depth)
    {
        if (depth >= MaxNesting)
        {
            throw FakeError.Validation("Nesting Levels have exceeded supported limits");
        }

        switch (value.Type)
        {
            case AttributeType.Number:
                return AttributeValue.WrapNumber(DecimalNumber.Parse(value.AsNumber()).ToString());
            case AttributeType.List:
                return AttributeValue.WrapList([.. value.AsList().Select(element => Normalize(element, depth + 1))]);
            case AttributeType.Map:
                var map = new Item(value.AsMap().Count);
                foreach (var (name, element) in value.AsMap())
                {
                    map.Add(name, Normalize(element, depth + 1));
                }

                return AttributeValue.WrapMap(map);
            case AttributeType.StringSet:
                ThrowIfDuplicates(value.AsStringSet(), value.AsStringSet(), StringComparer.Ordinal);
                return value;
            case AttributeType.NumberSet:
                string[] numbers = [.. value.AsNumberSet().Select(static number => DecimalNumber.Parse(number).ToString())];
                ThrowIfDuplicates(value.AsNumberSet(), numbers, StringComparer.Ordinal);
                return AttributeValue.WrapNumberSet(numbers);
            case AttributeType.BinarySet:
                string[] encoded = [.. value.AsBinarySet().Select(static element => Convert.ToBase64String(element.Span))];
                ThrowIfDuplicates(encoded, encoded, StringComparer.Ordinal);
                return value;
            default:
                return value;
        }
    }

    private static void ThrowIfDuplicates(IReadOnlyList<string> given, IReadOnlyList<string> keys, StringComparer comparer)
    {
        var seen = new HashSet<string>(comparer);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                throw FakeError.Validation($"One or more parameter values were invalid: Input collection [{string.Join(", ", given)}] contains duplicates.");
            }
        }
    }
}
