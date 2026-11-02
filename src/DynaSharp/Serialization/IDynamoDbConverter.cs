namespace DynaSharp.Serialization;

/// <summary>
/// Converts values of type <typeparamref name="T"/> to and from DynamoDB attribute values.
/// </summary>
/// <typeparam name="T">The .NET type.</typeparam>
/// <remarks>
/// <para>
/// Converters are static: the source generator calls them directly through the type parameter, without instances,
/// virtual calls or delegates. Built-in converters and <see cref="AttributeValue"/> use the same contract; they are
/// structs, because a struct type argument gives every use its own compiled code, so the call is direct and can be
/// inlined, where a class would share one instantiation and look the method up at run time. Implement it on a
/// <c>readonly record struct</c> as well, which also satisfies CA1815 (static classes cannot implement interfaces; a class works but is slower), and reference it from the entity,
/// for example to store a <see cref="Uri"/> as its original text:
/// </para>
/// <code>
/// public readonly record struct UriConverter : IDynamoDbConverter&lt;Uri&gt;
/// {
///     public static void Write(DynamoDbWriter writer, Uri value) =>
///         writer.WriteString(value.OriginalString);
///
///     public static Uri Read(ref DynamoDbReader reader, AttributeType type)
///     {
///         ConverterChecks.Expect(type, AttributeType.String);
///         return new Uri(reader.GetString(), UriKind.Absolute);
///     }
/// }
/// </code>
/// <para>
/// A converter that cannot read a value throws <see cref="System.Text.Json.JsonException"/>,
/// <see cref="FormatException"/>, <see cref="OverflowException"/> or <see cref="InvalidOperationException"/> (a
/// <see cref="UriFormatException"/> is a <see cref="FormatException"/>); the code mapping an entity reports exactly those, with
/// the name of the attribute, and lets every other exception through as a bug. Keep values out of messages: they end up in logs.
/// A converter of a type that is a nullable value or reference gets the null handling from
/// <see cref="NullableConverter{T, TConverter}"/> and <see cref="NullableReferenceConverter{T, TConverter}"/>; implement the converter
/// of the non-null type.
/// </para>
/// </remarks>
public interface IDynamoDbConverter<T>
{
    /// <summary>Writes a value as one complete attribute value, for example with <see cref="DynamoDbWriter.WriteNumber{TNumber}(TNumber)"/>.</summary>
    /// <param name="writer">The writer, positioned where a value is expected (after an attribute name, or inside a list). It is a ref struct, so it is never null and needs no check.</param>
    /// <param name="value">The value.</param>
    public static abstract void Write(DynamoDbWriter writer, T value);

    /// <summary>Reads the attribute value the reader is positioned on.</summary>
    /// <param name="reader">
    /// The reader, positioned on the value after <see cref="DynamoDbReader.ReadStartAttribute"/> or
    /// <see cref="DynamoDbReader.TryReadStartAttribute"/>. The caller reads the closing brace afterwards, so the
    /// converter must read the value completely (all elements of a list, map or set) and nothing more.
    /// </param>
    /// <param name="type">
    /// The DynamoDB type of the value. Check it with <see cref="ConverterChecks.Expect"/>, which throws
    /// <see cref="System.Text.Json.JsonException"/> for types the converter does not support.
    /// <see cref="AttributeType.Null"/> is passed to the converter too, so it can map it to
    /// <see langword="null"/> where that makes sense.
    /// </param>
    /// <returns>The value.</returns>
    public static abstract T Read(ref DynamoDbReader reader, AttributeType type);

    /// <summary>
    /// Finds out whether a command by handle treats a value like null, which <see cref="Mapping.OmitEmptyAttribute"/> asks for through <see cref="OmitEmptyConverter{TConverter}"/>. No other converter has a use for
    /// this: the default answers <see langword="false"/>, and a struct converter compiles that away, so a handle pays nothing.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the value counts as null.</returns>
    public static virtual bool IsOmitted(T value) => false;

    /// <summary>
    /// Finds out what a read of one attribute gives when the item does not have it. No value is the answer for most types, and the read then throws <see cref="DynamoDbMappingException"/> rather than
    /// make one up. A nullable type answers with null, because null is left out on write, and a set with an empty set, because DynamoDB has none.
    /// </summary>
    /// <remarks>
    /// This is infrastructure of <c>Select</c>, which alone asks it, and the built-in converters answer for themselves. A converter of your own needs it in one case: a converter of a set, because an empty set
    /// is a missing attribute in DynamoDB, so without an answer here a <c>Select</c> throws for every item whose set is empty. A read of the entity does not ask this: a property with a converter of your own keeps its
    /// initial value, or throws if it is <c>required</c>.
    /// </remarks>
    /// <param name="value">The value, if there is one.</param>
    /// <returns><see langword="true"/> if a missing attribute has a value of this type.</returns>
    public static virtual bool TryReadMissing(out T value)
    {
        value = default!;
        return false;
    }
}
