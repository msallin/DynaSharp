namespace DynaSharp.Mapping;

/// <summary>
/// Selects the converter of a type for every mapped type of an assembly, where a <see cref="DynamoDbConverterAttribute"/> would have to go on every property:
/// <c>[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(StoredTimestampConverter))]</c>. It can be repeated, once per type.
/// </summary>
/// <remarks>
/// <para>
/// The first match wins: <see cref="DynamoDbConverterAttribute"/> on the property, on the type of the value, this attribute, the built-in converters. It covers the type, its nullable form and elements of lists, dictionaries and
/// arrays, wherever the generator looks for the converter of a value. An element of a set needs a converter that also implements <see cref="Serialization.IDynamoDbSetElementConverter{T}"/>, or the set is <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA003</see>.
/// </para>
/// <para>
/// In a key it does what a <see cref="DynamoDbConverterAttribute"/> on the property does. A plain key goes through it, and its type need not be one DynamoDB can hold, so <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA017</see> does not apply. A template
/// has formats of its own, so a placeholder ignores it, and the attribute of the property, if it is stored, goes through it; on a placeholder that holds a string, <see cref="Guid"/>, <see cref="DateOnly"/> or enum by name, where the key and the attribute would hold different text, that is
/// <see href="https://msallin.github.io/DynaSharp/diagnostics.html#key-templates">DYNA126</see>. A <see cref="VersionAttribute"/> property keeps the built-in converter, because an update
/// has to add to it.
/// </para>
/// <para>
/// A converter that does not fit is <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA008</see>. Two defaults for one type, and a type that is nullable or an open generic, are <see href="https://msallin.github.io/DynaSharp/diagnostics.html#entities">DYNA029</see>. The converter implements
/// <see cref="Serialization.IDynamoDbConverter{T}"/> for the value type without nullability, as <see cref="DynamoDbConverterAttribute"/> says.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class DynamoDbDefaultConverterAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DynamoDbDefaultConverterAttribute"/> class.</summary>
    /// <param name="valueType">The type the converter is for, without nullability.</param>
    /// <param name="converterType">The converter type, closed over its type arguments.</param>
    public DynamoDbDefaultConverterAttribute(Type valueType, Type converterType)
    {
        ValueType = valueType;
        ConverterType = converterType;
    }

    /// <summary>Gets the type the converter is for.</summary>
    public Type ValueType { get; }

    /// <summary>Gets the converter type.</summary>
    public Type ConverterType { get; }
}
