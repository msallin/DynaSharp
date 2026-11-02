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
    /// Finds out whether a write treats a value like null, which leaves the attribute out. It is for a value that DynamoDB would refuse, or that should not be stored, such as a list stored as a set, which cannot be
    /// empty: <c>public static bool IsOmitted(List&lt;string&gt; value) =&gt; value.Count == 0;</c>. <see cref="Mapping.OmitEmptyAttribute"/> asks the same question through <see cref="OmitEmptyConverter{TConverter}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every write of the property asks: a <c>Put</c> of the entity, <c>Item.From</c>, a batch, a step of a transaction and a nested document leave the attribute out, <c>Set</c> by handle is a <c>Remove</c>,
    /// <c>SetIfNotNull</c> adds nothing and <c>SetIfNotExists</c> rejects the value with an <see cref="ArgumentException"/>. A raw path writes what it is given. A nullable property asks with a value that is not null.
    /// </para>
    /// <para>
    /// The default answers <see langword="false"/>. The source generator emits the call only for a <c>[DynamoDbConverter]</c> that implements this, implicitly or explicitly, so every other property pays nothing, and a
    /// handle compiles the question away. It is the property that a converter answers for: a property that feeds a key is always written, because a key cannot do without it, and an element of a list or a map is
    /// always written, because a list has positions.
    /// </para>
    /// <para>
    /// A read does not ask. The item has no attribute, so a read of the entity keeps the initial value of the property, and throws <see cref="DynamoDbMappingException"/> if it is <c>required</c>. Give the property an
    /// initial value that means the same as what was left out, and implement <see cref="TryReadMissing"/> for <c>Select</c>.
    /// </para>
    /// </remarks>
    /// <param name="value">The value, never <see langword="null"/> on a write.</param>
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
