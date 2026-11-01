using System.Numerics;
using DynaSharp.Expressions;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>What a handle can do depending on the type of its attribute.</summary>
public static class AttrExtensions
{
    private const string NoText = "begins_with is for strings and binary values, and DynamoDB answers with no item for this attribute. Compare it with Eq, Gt or Between.";

    private const string BinaryPrefix = "DynamoDB compares a binary attribute with a binary prefix, and answers with no item for a string. Pass the bytes.";

    /// <summary>
    /// Checks with <c>begins_with</c>, which is for strings and compares bytes. An enum, a <see cref="Guid"/> and a date are stored as strings, so it works on them, a number or a boolean does not
    /// compile, and a binary attribute takes a binary prefix.
    /// </summary>
    /// <typeparam name="T">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith<T, TConverter>(this Attr<T, TConverter> attribute, string prefix)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a binary attribute, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix, which the condition holds without a copy.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<byte[], BinaryConverter> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a binary attribute, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix, which the condition holds without a copy.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a nullable binary attribute, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix, which the condition holds without a copy.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<byte[], OptionalReferenceConverter<byte[], BinaryConverter>> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a nullable binary attribute, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix, which the condition holds without a copy.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<ReadOnlyMemory<byte>, OptionalConverter<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because a string prefix never matches a binary value.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(BinaryPrefix, error: true)]
    public static BeginsWith BeginsWith(this Attr<byte[], BinaryConverter> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because a string prefix never matches a binary value.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(BinaryPrefix, error: true)]
    public static BeginsWith BeginsWith(this Attr<byte[], OptionalReferenceConverter<byte[], BinaryConverter>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because a string prefix never matches a binary value.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(BinaryPrefix, error: true)]
    public static BeginsWith BeginsWith(this Attr<ReadOnlyMemory<byte>, OptionalConverter<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because a string prefix never matches a binary value.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(BinaryPrefix, error: true)]
    public static BeginsWith BeginsWith(this Attr<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because DynamoDB finds nothing with <c>begins_with</c> on a number. Overload resolution prefers this to the general form for a handle of a number.</summary>
    /// <typeparam name="T">The type of the number.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(NoText, error: true)]
    public static BeginsWith BeginsWith<T>(this Attr<T, NumberConverter<T>> attribute, string prefix)
        where T : INumberBase<T> => new(attribute.Path, prefix);

    /// <summary>Does not compile, because DynamoDB finds nothing with <c>begins_with</c> on a number.</summary>
    /// <typeparam name="T">The type of the number.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(NoText, error: true)]
    public static BeginsWith BeginsWith<T>(this Attr<T, OptionalConverter<T, NumberConverter<T>>> attribute, string prefix)
        where T : struct, INumberBase<T> => new(attribute.Path, prefix);

    /// <summary>Does not compile, because DynamoDB finds nothing with <c>begins_with</c> on a timestamp stored as a number.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(NoText, error: true)]
    public static BeginsWith BeginsWith(this Attr<DateTimeOffset, EpochSecondsConverter> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because DynamoDB finds nothing with <c>begins_with</c> on a timestamp stored as a number.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(NoText, error: true)]
    public static BeginsWith BeginsWith(this Attr<DateTimeOffset, OptionalConverter<DateTimeOffset, EpochSecondsConverter>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because DynamoDB finds nothing with <c>begins_with</c> on a boolean.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(NoText, error: true)]
    public static BeginsWith BeginsWith(this Attr<bool, BoolConverter> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because DynamoDB finds nothing with <c>begins_with</c> on a boolean.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(NoText, error: true)]
    public static BeginsWith BeginsWith(this Attr<bool, OptionalConverter<bool, BoolConverter>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>
    /// Gets an attribute inside a nested document, by the handle that its document type generates:
    /// <c>Order.Attrs.ShipTo.At(Address.Attrs.City)</c> has the path <c>ShipTo.City</c>, and it can go on from there. A handle that names a
    /// whole document stays an ordinary attribute, which is why this is a call: <c>Set(Order.Attrs.ShipTo, address)</c> replaces the map.
    /// </summary>
    /// <typeparam name="TDocument">The document. A handle of a scalar has nothing inside, so it does not compile.</typeparam>
    /// <typeparam name="TConverter">The converter of the document.</typeparam>
    /// <typeparam name="TValue">The type of the inner attribute.</typeparam>
    /// <typeparam name="TValueConverter">The converter of the inner attribute.</typeparam>
    /// <param name="document">The attribute that holds the document.</param>
    /// <param name="attribute">The attribute inside the document, a name or a path. Nothing checks that it belongs to <typeparamref name="TDocument"/>.</param>
    /// <returns>The attribute at the nested path.</returns>
    public static Attr<TValue, TValueConverter> At<TDocument, TConverter, TValue, TValueConverter>(this Attr<TDocument, TConverter> document, Attr<TValue, TValueConverter> attribute)
        where TDocument : IDynamoDbDocument<TDocument>
        where TConverter : IDynamoDbConverter<TDocument>
        where TValueConverter : IDynamoDbConverter<TValue> => new(document.Path + "." + attribute.Path);
}
