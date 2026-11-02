namespace DynaSharp.Generators;

/// <summary>A mapped property.</summary>
/// <param name="Name">The name, for example <c>Total</c>.</param>
/// <param name="Identifier">The name as code, for example <c>Total</c> or <c>@class</c>.</param>
/// <param name="ValueType">The declared type as code, with its nullability.</param>
/// <param name="AttributeName">The DynamoDB attribute. Unused when <see cref="RestoredFromKey"/> is set.</param>
/// <param name="AttributeNameUtf8Length">The bytes of <paramref name="AttributeName"/>, the switch key of reads.</param>
/// <param name="Converter">The converters, or null when <see cref="RestoredFromKey"/> is set.</param>
/// <param name="Write">When the property is written.</param>
/// <param name="Missing">What a read does without the attribute.</param>
/// <param name="IsConstructorParameter">Whether the chosen constructor supplies the value.</param>
/// <param name="IsWriteOnly">A computed property forced into the item: written, never read back.</param>
/// <param name="RestoredFromKey">For a <c>[KeyOnly]</c> property, the attribute of the key to parse it from; otherwise null.</param>
/// <param name="FallbackKey">For a stored property that a key holds without loss, the attribute of that key, which a read parses when the attribute of the property is missing; otherwise null.</param>
/// <param name="ParameterName">The parameter of the property in <c>Keys</c>, as code; see <see cref="PlaceholderSegment.ParameterName"/>.</param>
/// <param name="IsPublic">Whether the property, its type and its converters are all public, so that a public handle can name them.</param>
/// <param name="OmitEmptyConverter">
/// With <c>[OmitEmpty]</c>, the converter of the handle as code up to its last type argument, which is the converter of the property: an empty value counts as null for the property as for its handle. Null without it.
/// </param>
/// <param name="ReadsMissingAsZero">Whether this is a version that takes an item without one for version 0, for a read of the entity as for its handle.</param>
internal sealed record PropertyModel(
    string Name,
    string Identifier,
    string ValueType,
    string AttributeName,
    int AttributeNameUtf8Length,
    ConverterModel? Converter,
    WriteCondition Write,
    MissingBehavior Missing,
    bool IsConstructorParameter,
    bool IsWriteOnly,
    string? RestoredFromKey,
    string? FallbackKey,
    string ParameterName,
    bool IsPublic,
    string? OmitEmptyConverter,
    bool ReadsMissingAsZero)
{
    /// <summary>Returns the type as code without its nullability, which is what a handle and a key take: a key has no null.</summary>
    /// <returns>The type.</returns>
    public string InnerValueType() => ValueType.EndsWith("?", StringComparison.Ordinal) ? ValueType.Substring(0, ValueType.Length - 1) : ValueType;
}
