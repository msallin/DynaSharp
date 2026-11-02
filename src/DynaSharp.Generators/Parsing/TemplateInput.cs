namespace DynaSharp.Generators;

/// <summary>A property as a template sees it.</summary>
/// <param name="Property">The property name.</param>
/// <param name="ParameterName">The builder parameter as code.</param>
/// <param name="ValueType">The type without nullability, as code.</param>
/// <param name="TypeName">The type without nullability, as messages show it.</param>
/// <param name="Type">The formats of the type.</param>
/// <param name="IsNullable">Whether the property can be null.</param>
/// <param name="IsComputed">Whether the property has no setter and no constructor parameter.</param>
/// <param name="HasDependencies">Whether <c>[ComputedFrom]</c> says what a computed property is derived from, which is what an index template asks of it.</param>
/// <param name="IsAccessible">Whether a public builder of the entity can take the type as a parameter.</param>
internal sealed record TemplateInput(
    string Property,
    string ParameterName,
    string ValueType,
    string TypeName,
    KeyInputType Type,
    bool IsNullable,
    bool IsComputed,
    bool HasDependencies,
    bool IsAccessible);
