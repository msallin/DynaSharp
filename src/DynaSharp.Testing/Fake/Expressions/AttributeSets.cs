namespace DynaSharp.Testing.Fake;

/// <summary>The elements of a set or a list as values, and a set made from values: what <c>contains</c>, <c>ADD</c> and <c>DELETE</c> work on.</summary>
internal static class AttributeSets
{
    /// <summary>Tells whether a type is one of the three sets.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> for SS, NS and BS.</returns>
    public static bool IsSet(AttributeType type) => type is AttributeType.StringSet or AttributeType.NumberSet or AttributeType.BinarySet;

    /// <summary>Gets the elements of a set or a list.</summary>
    /// <param name="value">A set or a list.</param>
    /// <returns>The elements: strings of a string set, numbers of a number set, binaries of a binary set, the elements of a list.</returns>
    public static IEnumerable<AttributeValue> Elements(AttributeValue value) => value.Type switch
    {
        AttributeType.StringSet => value.AsStringSet().Select(AttributeValue.FromString),
        AttributeType.NumberSet => value.AsNumberSet().Select(static number => AttributeValue.FromNumber(number)),
        AttributeType.BinarySet => value.AsBinarySet().Select(static element => AttributeValue.FromBinary(element.Span)),
        _ => value.AsList(),
    };

    /// <summary>Makes a set of the elements.</summary>
    /// <param name="type">The type of the set.</param>
    /// <param name="elements">The elements, at least one, of the element type, without duplicates.</param>
    /// <returns>The set.</returns>
    public static AttributeValue Create(AttributeType type, IEnumerable<AttributeValue> elements) => type switch
    {
        AttributeType.StringSet => AttributeValue.FromStringSet(elements.Select(static element => element.AsString())),
        AttributeType.NumberSet => AttributeValue.FromNumberSet(elements.Select(static element => element.AsNumber())),
        _ => AttributeValue.FromBinarySet(elements.Select(static element => element.AsBinary().ToArray())),
    };
}
