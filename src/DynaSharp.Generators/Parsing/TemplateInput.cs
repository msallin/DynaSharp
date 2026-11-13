namespace DynaSharp.Generators;

/// <summary>A property as a template sees it.</summary>
/// <param name="Property">The property name.</param>
/// <param name="ParameterName">The builder parameter as code.</param>
/// <param name="ValueType">The type without nullability, as code.</param>
/// <param name="TypeName">The type without nullability, as messages show it.</param>
/// <param name="Type">The formats of the type.</param>
/// <param name="IsNullable">Whether the property can be null.</param>
/// <param name="OmitsEmpty">Whether the property is a string with an <c>[OmitEmpty]</c> of its own, so that an empty value is left out as null is.</param>
/// <param name="IsComputed">Whether the property has no setter and no constructor parameter.</param>
/// <param name="HasDependencies">Whether <c>[ComputedFrom]</c> says what a computed property is derived from, which is what an index template asks of it.</param>
/// <param name="IsAccessible">Whether a public builder of the entity can take the type as a parameter.</param>
/// <param name="Converter">The converter of its own that writes the text of the property, as code, or null for the built-in or generated one.</param>
internal sealed record TemplateInput(
    string Property,
    string ParameterName,
    string ValueType,
    string TypeName,
    KeyInputType Type,
    bool IsNullable,
    bool OmitsEmpty,
    bool IsComputed,
    bool HasDependencies,
    bool IsAccessible,
    string? Converter);
