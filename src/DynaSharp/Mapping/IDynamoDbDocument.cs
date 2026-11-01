using DynaSharp.Serialization;

namespace DynaSharp.Mapping;

/// <summary>
/// A type that knows how to write itself as DynamoDB attributes and read itself back: a generated entity, a generated nested document,
/// or <see cref="Item"/>. It is what makes one <see cref="Table{T}"/> serve all of them, because <typeparamref name="TSelf"/> decides
/// only how an item is written and read.
/// </summary>
/// <remarks>
/// <para>
/// Implement it with explicit interface members, so nothing lands on the public surface of the type. The source generator does that for
/// <c>[DynamoDbEntity]</c> and <c>[DynamoDbDocument]</c> types; write it by hand for a type the generator cannot see.
/// </para>
/// <para>
/// The methods work without the surrounding braces, so one implementation serves a top-level item, an element of an array of items and a
/// nested map.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface IDynamoDbDocument<TSelf>
    where TSelf : IDynamoDbDocument<TSelf>
{
    /// <summary>Writes the attribute names and values, without the surrounding braces.</summary>
    /// <param name="writer">The writer, positioned inside an item or a map.</param>
    /// <param name="entity">The value to write.</param>
    public static abstract void WriteAttributes(DynamoDbWriter writer, TSelf entity);

    /// <summary>Reads the attributes after the opening brace, up to and including the closing one.</summary>
    /// <param name="reader">The reader, positioned after the opening brace.</param>
    /// <returns>The value.</returns>
    /// <exception cref="DynamoDbMappingException">An attribute is missing or cannot be converted, or the item is of another type.</exception>
    public static abstract TSelf ReadAttributes(ref DynamoDbReader reader);

    /// <summary>
    /// Writes the attributes as a put of a <see cref="VersionAttribute"/> entity stores them, with the version after the one the value holds. The value is left as
    /// it is, and <see cref="WriteAttributes"/> writes the version as it stands, so that a plain copy stays one. The generator implements this for a type with a
    /// version; a type written by hand that names a version attribute in its <see cref="DynamoDbEntityInfo"/> implements it too.
    /// </summary>
    /// <param name="writer">The writer, positioned inside an item.</param>
    /// <param name="entity">The value to write.</param>
    /// <returns>The version the value holds, which is the one the put expects to find.</returns>
    /// <exception cref="NotSupportedException">The type does not implement this.</exception>
    /// <exception cref="OverflowException">The next version does not fit the property.</exception>
    public static virtual long WriteNextVersion(DynamoDbWriter writer, TSelf entity) =>
        throw new NotSupportedException($"{typeof(TSelf)} names a version attribute and does not implement WriteNextVersion.");
}
