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
/// The members are static abstract, so a converter generic over its value, such as <c>JsonConverter&lt;T&gt;</c>, declares static members on a generic type. That is <c>CA1000</c>, which fails a project that treats it as an
/// error. Write one struct per type over a generic helper, as here with a <c>TextValue.Write&lt;T&gt;</c> and a <c>TextValue.Read&lt;T&gt;</c> of your own:
/// </para>
/// <code>
/// public readonly record struct MoneyConverter : IDynamoDbConverter&lt;Money&gt;
/// {
///     public static void Write(DynamoDbWriter writer, Money value) => TextValue.Write(writer, value);
///
///     public static Money Read(ref DynamoDbReader reader, AttributeType type) => TextValue.Read&lt;Money&gt;(ref reader, type);
/// }
/// </code>
/// <para>
/// A converter that cannot read a value throws <see cref="System.Text.Json.JsonException"/>,
/// <see cref="FormatException"/>, <see cref="OverflowException"/> or <see cref="InvalidOperationException"/> (a
/// <see cref="UriFormatException"/> is a <see cref="FormatException"/>); the code mapping an entity reports exactly those, with
/// the name of the attribute, and lets every other exception through as a bug. Keep values out of messages: they end up in logs.
/// A converter of a type that is a nullable value or reference gets the null handling from
/// <see cref="NullableConverter{T, TConverter}"/> and <see cref="NullableReferenceConverter{T, TConverter}"/>; implement the converter
/// of the non-null type. To let a read of an item go on past a value that means nothing to the property, implement <see cref="CountsAsMissing"/> as well.
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
    /// empty: <c>public static bool IsOmitted(List&lt;string&gt; value) =&gt; value.Count == 0;</c>. <see cref="Mapping.OmitEmptyAttribute"/> asks the same question through <see cref="OmitEmptyConverter{TConverter}"/> and
    /// <see cref="OmitEmptyCollectionConverter{T, TElement, TConverter}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every write of the property asks: a <c>Put</c> of the entity, <c>Item.From</c>, a batch, a step of a transaction and a nested document leave the attribute out, <c>Set</c> by handle is a <c>Remove</c>,
    /// <c>SetIfNotNull</c> adds nothing and <c>SetIfNotExists</c> rejects the value with an <see cref="ArgumentException"/>. A raw path writes what it is given. A nullable property asks with a value that is not null.
    /// </para>
    /// <para>
    /// The default answers <see langword="false"/>. The source generator emits the call only for a <c>[DynamoDbConverter]</c> or a <c>[DynamoDbDefaultConverter]</c> that implements this, implicitly or explicitly, so every other property pays nothing, and a
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
    /// This is infrastructure of <c>Select</c>, which alone asks it, and the built-in converters and the generated handle of a version with <see cref="Mapping.VersionMissing.Allow"/> answer for themselves. A converter of your own needs it in one case: a converter of a set, because an empty set
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

    /// <summary>
    /// Finds out whether a stored value counts as if the attribute were not in the item. The mirror of <see cref="IsOmitted"/> for a read: it is for a value that means nothing to the property, such as a name an
    /// enum no longer has, or a blank string where a JSON payload belongs, so that one bad attribute does not fail the read of the item.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A lenient enum: a name the type does not have keeps the initial value. <see cref="Read"/> stays strict, because a list has positions and asks nothing. The value is parsed twice, here and in <see cref="Read"/>, and only on a property whose converter answers.
    /// An enum that the generated converter stores by name needs none of this: <see cref="Mapping.DynamoDbEnumAttribute"/> on the enum gives that converter the same answer. This one is for an enum stored otherwise.
    /// </para>
    /// <code>
    /// public readonly record struct LenientPositionConverter : IDynamoDbConverter&lt;Position&gt;
    /// {
    ///     public static void Write(DynamoDbWriter writer, Position value) =>
    ///         writer.WriteString(value.ToString());
    ///
    ///     public static Position Read(ref DynamoDbReader reader, AttributeType type)
    ///     {
    ///         ConverterChecks.Expect(type, AttributeType.String);
    ///         return Enum.TryParse&lt;Position&gt;(reader.GetString(), out var value) ? value : throw new FormatException("Not a position.");
    ///     }
    ///
    ///     public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) =>
    ///         type == AttributeType.String &amp;&amp; !Enum.TryParse&lt;Position&gt;(reader.GetString(), out _);
    /// }
    /// </code>
    /// <para>
    /// A blank string as null: a <c>Payload?</c> stored as JSON text reads as null for a stored <c>""</c>. The value is looked at twice here as well, first for its length and then by the parser, and only on a property whose converter answers.
    /// </para>
    /// <code>
    /// public readonly record struct PayloadConverter : IDynamoDbConverter&lt;Payload&gt;
    /// {
    ///     public static void Write(DynamoDbWriter writer, Payload value) =>
    ///         writer.WriteString(JsonSerializer.Serialize(value, ShopJson.Default.Payload));
    ///
    ///     public static Payload Read(ref DynamoDbReader reader, AttributeType type)
    ///     {
    ///         ConverterChecks.Expect(type, AttributeType.String);
    ///         return JsonSerializer.Deserialize(reader.GetStringUtf8(), ShopJson.Default.Payload)!;
    ///     }
    ///
    ///     public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) =>
    ///         type == AttributeType.String &amp;&amp; reader.GetStringUtf8().IsEmpty;
    /// }
    /// </code>
    /// <para>
    /// The answer <see langword="true"/> means as if the attribute were not in the item, whatever the property is: it keeps its initial value, and a nullable one does too, so an initializer on a <c>Payload?</c> stays and only one without gives null.
    /// A <c>required</c> one throws <see cref="DynamoDbMappingException"/>, and an input of a key that holds it is restored from that key. <c>Select</c> gives what <see cref="TryReadMissing"/> gives, which is null for a nullable handle, also where a read of the entity keeps
    /// an initializer, and otherwise a throw that <c>DYNA209</c> reports. <c>Return</c> throws, as it does for a missing attribute. A value that is <c>{"NULL":true}</c> is passed here as <see cref="AttributeType.Null"/>, except on a nullable property, which reads it as null without asking.
    /// </para>
    /// <para>
    /// The default answers <see langword="false"/>. The source generator emits the question only for a <c>[DynamoDbConverter]</c> or a <c>[DynamoDbDefaultConverter]</c> that implements this, implicitly or explicitly, and for the converter it writes for an enum with
    /// <c>[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]</c>, so every other property pays nothing, and a handle compiles the question away. It is the property that a converter answers for: an element of a list, a map or a set is not asked, because a list has positions, and neither is a plain key, because a read that took it for missing would give an entity with another
    /// key. <c>Keys.X.Parse</c> and <see cref="Item"/> do not ask either.
    /// </para>
    /// <para>
    /// The reader stands on the value, as it does for <see cref="Read"/>, and this cannot move it, so <see cref="Read"/> finds the value where it was, and skipping a value that counts as missing is not the converter's job. On an <see langword="in"/> reader the getters work
    /// as they always do, a call that moves a reader compiles and moves a copy that the compiler makes for that one call, and passing it on as <see langword="ref"/> does not compile. So to look inside a list or a map, copy the reader once and walk the copy,
    /// because a loop over the parameter itself starts over with every call and never ends. A copy is less than 200 bytes on the stack and allocates nothing:
    /// </para>
    /// <code>
    /// public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type)
    /// {
    ///     if (type != AttributeType.Map)
    ///     {
    ///         return false;
    ///     }
    ///
    ///     var map = reader;
    ///     while (map.TryReadAttributeName())
    ///     {
    ///         if (map.AttributeNameEquals("body"u8))
    ///         {
    ///             return map.ReadStartAttribute() == AttributeType.String &amp;&amp; map.GetStringUtf8().IsEmpty;
    ///         }
    ///
    ///         map.SkipAttributeValue();
    ///     }
    ///
    ///     return true;
    /// }
    /// </code>
    /// <para>This can throw what <see cref="Read"/> can, with the same result. <see cref="Read"/> is not called for a value counted as missing.</para>
    /// </remarks>
    /// <param name="reader">The reader, positioned on the value. Read it with the getters, which do not move it.</param>
    /// <param name="type">The DynamoDB type of the value.</param>
    /// <returns><see langword="true"/> if the value counts as a missing attribute.</returns>
    public static virtual bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => false;
}
