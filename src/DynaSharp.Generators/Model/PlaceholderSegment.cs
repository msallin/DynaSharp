namespace DynaSharp.Generators;

/// <summary>A property in a key template.</summary>
/// <param name="Property">The property, by <see cref="PropertyModel.Name"/>.</param>
/// <param name="ParameterName">The builder parameter as code, for example <c>createdAt</c> or <c>@class</c>; the same for a property across all keys.</param>
/// <param name="ValueType">The builder parameter type as code. Never nullable: a builder needs the value.</param>
/// <param name="Encoding">How the value becomes text and back.</param>
/// <param name="InputIsNullable">Whether the property itself is nullable, which makes the key sparse.</param>
internal sealed record PlaceholderSegment(
    string Property,
    string ParameterName,
    string ValueType,
    KeyEncoding Encoding,
    bool InputIsNullable) : TemplateSegment;
