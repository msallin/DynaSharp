namespace DynaSharp.Mapping;

/// <summary>
/// Selects the converter of a property, or of a type wherever it is used, including inside collections. A converter on a property wins
/// over one on the property's type, and both win over <see cref="DynamoDbDefaultConverterAttribute"/> and the built-in converters.
/// </summary>
/// <remarks>
/// The converter implements <see cref="Serialization.IDynamoDbConverter{T}"/> for the value type without nullability: a converter
/// on a <c>DateTimeOffset?</c> property implements it for <see cref="DateTimeOffset"/>, and the generator adds the null handling.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class DynamoDbConverterAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DynamoDbConverterAttribute"/> class.</summary>
    /// <param name="converterType">The converter type, closed over its type arguments.</param>
    public DynamoDbConverterAttribute(Type converterType)
    {
        ConverterType = converterType;
    }

    /// <summary>Gets the converter type.</summary>
    public Type ConverterType { get; }
}
