namespace DynaSharp.Generators;

/// <summary>A constructor parameter, bound to the property that supplies its value.</summary>
/// <param name="PropertyName">The property, by <see cref="PropertyModel.Name"/>.</param>
/// <param name="DefaultLiteral">The default as code, used when the attribute is missing, or null if the parameter has none.</param>
internal sealed record ConstructorParameter(string PropertyName, string? DefaultLiteral);
