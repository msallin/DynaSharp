namespace DynaSharp.Serialization;

/// <summary>
/// Reads and writes what <typeparamref name="TConverter"/> does, and counts a stored empty string, <c>{"S":""}</c>, as if the attribute were not in the item, on top of what
/// <typeparamref name="TConverter"/> counts. It is for a table where other code wrote <c>""</c> for no value, into an attribute whose converter cannot read it:
/// <c>[DynamoDbConverter(typeof(EmptyStringAsMissingConverter&lt;DateTimeOffset, DateTimeOffsetConverter&gt;))] public DateTimeOffset? ShippedAt { get; init; }</c>
/// reads <c>{"S":""}</c> as null, and <c>{"S":"2026-10-31T12:00:00.0000000Z"}</c> as that instant.
/// </summary>
/// <typeparam name="T">The type of the property.</typeparam>
/// <typeparam name="TConverter">The converter that reads and writes the values, a built-in or one of your own.</typeparam>
/// <remarks>
/// <para>
/// As if the attribute were not in the item means what <see cref="IDynamoDbConverter{T}.CountsAsMissing"/> says: the property keeps its initial value, a nullable one too, and a
/// <c>required</c> one throws <see cref="DynamoDbMappingException"/>. Only the property is asked: an element of a list, a map or a set, a plain key and <see cref="Item"/> read the
/// empty string with <typeparamref name="TConverter"/>, which may throw. A write never stores <c>""</c> for a value unless <typeparamref name="TConverter"/> does.
/// </para>
/// <para>
/// It passes <see cref="IDynamoDbConverter{T}.IsOmitted"/> on, and not <see cref="IDynamoDbConverter{T}.TryReadMissing"/>: an answer there would tell <c>DYNA209</c> that a
/// <c>Select</c> of the property cannot throw, and for a value that is not nullable it throws on an empty string. A nullable property gets null from its wrapper of null, as without this.
/// </para>
/// <para>
/// It costs a property one look at the length of the text before the read, which allocates nothing. Naming a generic converter in an attribute is no <c>CA1000</c>, which is about
/// declaring one.
/// </para>
/// </remarks>
public readonly struct EmptyStringAsMissingConverter<T, TConverter> : IDynamoDbConverter<T>
    where TConverter : IDynamoDbConverter<T>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, T value) => TConverter.Write(writer, value);

    /// <inheritdoc/>
    public static T Read(ref DynamoDbReader reader, AttributeType type) => TConverter.Read(ref reader, type);

    /// <inheritdoc/>
    public static bool IsOmitted(T value) => TConverter.IsOmitted(value);

    /// <inheritdoc/>
    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) =>
        (type == AttributeType.String && reader.GetStringUtf8().IsEmpty) || TConverter.CountsAsMissing(in reader, type);
}
