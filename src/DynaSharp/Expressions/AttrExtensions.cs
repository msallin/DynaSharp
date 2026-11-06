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
    /// <param name="prefix">The prefix bytes. The condition keeps a reference to this memory, so do not change it before the command is sent.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<byte[], BinaryConverter> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a binary attribute, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix bytes. The condition keeps a reference to this memory, so do not change it before the command is sent.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a nullable binary attribute, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix bytes. The condition keeps a reference to this memory, so do not change it before the command is sent.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<byte[], NullableReferenceHandleConverter<byte[], BinaryConverter>> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a nullable binary attribute, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix bytes. The condition keeps a reference to this memory, so do not change it before the command is sent.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<ReadOnlyMemory<byte>, NullableHandleConverter<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a binary attribute with <see cref="OmitEmptyAttribute"/>, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix bytes. The condition keeps a reference to this memory, so do not change it before the command is sent.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>begins_with</c> on a nullable binary attribute with <see cref="OmitEmptyAttribute"/>, which compares bytes.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix bytes. The condition keeps a reference to this memory, so do not change it before the command is sent.</param>
    /// <returns>The condition.</returns>
    public static BeginsWith BeginsWith(this Attr<byte[], NullableReferenceHandleConverter<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>>> attribute, ReadOnlyMemory<byte> prefix) => new(attribute.Path, prefix);

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
    public static BeginsWith BeginsWith(this Attr<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because a string prefix never matches a binary value.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(BinaryPrefix, error: true)]
    public static BeginsWith BeginsWith(this Attr<byte[], NullableReferenceHandleConverter<byte[], OmitEmptyCollectionConverter<byte[], byte, BinaryConverter>>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because a string prefix never matches a binary value.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(BinaryPrefix, error: true)]
    public static BeginsWith BeginsWith(this Attr<byte[], NullableReferenceHandleConverter<byte[], BinaryConverter>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Does not compile, because a string prefix never matches a binary value.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    [Obsolete(BinaryPrefix, error: true)]
    public static BeginsWith BeginsWith(this Attr<ReadOnlyMemory<byte>, NullableHandleConverter<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>> attribute, string prefix) => new(attribute.Path, prefix);

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
    public static BeginsWith BeginsWith<T>(this Attr<T, NullableHandleConverter<T, NumberConverter<T>>> attribute, string prefix)
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
    public static BeginsWith BeginsWith(this Attr<DateTimeOffset, NullableHandleConverter<DateTimeOffset, EpochSecondsConverter>> attribute, string prefix) => new(attribute.Path, prefix);

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
    public static BeginsWith BeginsWith(this Attr<bool, NullableHandleConverter<bool, BoolConverter>> attribute, string prefix) => new(attribute.Path, prefix);

    /// <summary>Checks with <c>contains</c> whether a set holds an element, compared as the set stores it. An attribute that does not exist holds nothing, so the condition fails there and its <c>Negate()</c> holds, which makes <c>Attrs.Seen.Contains(id).Negate()</c> true for a new item.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<HashSet<T>, HashSetConverter<T, TElement>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a nullable property that is a <see cref="HashSet{T}"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<HashSet<T>, NullableReferenceHandleConverter<HashSet<T>, HashSetConverter<T, TElement>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a property that is a <see cref="HashSet{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<HashSet<T>, OmitEmptySetConverter<HashSet<T>, T, HashSetConverter<T, TElement>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a nullable property that is a <see cref="HashSet{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<HashSet<T>, NullableReferenceHandleConverter<HashSet<T>, OmitEmptySetConverter<HashSet<T>, T, HashSetConverter<T, TElement>>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a property that is an <see cref="IReadOnlySet{T}"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<IReadOnlySet<T>, ReadOnlySetConverter<T, TElement>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a nullable property that is an <see cref="IReadOnlySet{T}"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<IReadOnlySet<T>, NullableReferenceHandleConverter<IReadOnlySet<T>, ReadOnlySetConverter<T, TElement>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a property that is an <see cref="IReadOnlySet{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<IReadOnlySet<T>, OmitEmptySetConverter<IReadOnlySet<T>, T, ReadOnlySetConverter<T, TElement>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a nullable property that is an <see cref="IReadOnlySet{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<IReadOnlySet<T>, NullableReferenceHandleConverter<IReadOnlySet<T>, OmitEmptySetConverter<IReadOnlySet<T>, T, ReadOnlySetConverter<T, TElement>>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a property that is an array stored as a set holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<T[], SetArrayConverter<T, TElement>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a nullable property that is an array stored as a set holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<T[], NullableReferenceHandleConverter<T[], SetArrayConverter<T, TElement>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a property that is an array stored as a set with <see cref="OmitEmptyAttribute"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<T[], OmitEmptySetConverter<T[], T, SetArrayConverter<T, TElement>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the set of a nullable property that is an array stored as a set with <see cref="OmitEmptyAttribute"/> holds an element. See <see cref="Contains{T, TElement}(Attr{HashSet{T}, HashSetConverter{T, TElement}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TElement">The converter of the elements, which writes the operand as the set stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, SetElementValueConverter<T, TElement>> Contains<T, TElement>(this Attr<T[], NullableReferenceHandleConverter<T[], OmitEmptySetConverter<T[], T, SetArrayConverter<T, TElement>>>> attribute, T element)
        where TElement : IDynamoDbSetElementConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether a list holds an element equal to the value, which DynamoDB compares by type and value. An attribute that does not exist holds nothing, so the condition fails there and its <c>Negate()</c> holds.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<List<T>, ListConverter<T, TConverter>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is a <see cref="List{T}"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<List<T>, NullableReferenceHandleConverter<List<T>, ListConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a property that is a <see cref="List{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<List<T>, OmitEmptyCollectionConverter<List<T>, T, ListConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is a <see cref="List{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<List<T>, NullableReferenceHandleConverter<List<T>, OmitEmptyCollectionConverter<List<T>, T, ListConverter<T, TConverter>>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a property that is an <see cref="IReadOnlyList{T}"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IReadOnlyList<T>, ReadOnlyListConverter<T, TConverter>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is an <see cref="IReadOnlyList{T}"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IReadOnlyList<T>, NullableReferenceHandleConverter<IReadOnlyList<T>, ReadOnlyListConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a property that is an <see cref="IReadOnlyList{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IReadOnlyList<T>, OmitEmptyCollectionConverter<IReadOnlyList<T>, T, ReadOnlyListConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is an <see cref="IReadOnlyList{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IReadOnlyList<T>, NullableReferenceHandleConverter<IReadOnlyList<T>, OmitEmptyCollectionConverter<IReadOnlyList<T>, T, ReadOnlyListConverter<T, TConverter>>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a property that is an <see cref="IList{T}"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IList<T>, MutableListConverter<T, TConverter>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is an <see cref="IList{T}"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IList<T>, NullableReferenceHandleConverter<IList<T>, MutableListConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a property that is an <see cref="IList{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IList<T>, OmitEmptyCollectionConverter<IList<T>, T, MutableListConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is an <see cref="IList{T}"/> with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<IList<T>, NullableReferenceHandleConverter<IList<T>, OmitEmptyCollectionConverter<IList<T>, T, MutableListConverter<T, TConverter>>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a property that is an array holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<T[], ArrayConverter<T, TConverter>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is an array holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<T[], NullableReferenceHandleConverter<T[], ArrayConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a property that is an array with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<T[], OmitEmptyCollectionConverter<T[], T, ArrayConverter<T, TConverter>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether the list of a nullable property that is an array with <see cref="OmitEmptyAttribute"/> holds an element equal to the value. See <see cref="Contains{T, TConverter}(Attr{List{T}, ListConverter{T, TConverter}}, T)"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <typeparam name="TConverter">The converter of the elements, which writes the operand as the list stores it.</typeparam>
    /// <param name="attribute">The attribute.</param>
    /// <param name="element">The element.</param>
    /// <returns>The condition.</returns>
    public static Contains<T, TConverter> Contains<T, TConverter>(this Attr<T[], NullableReferenceHandleConverter<T[], OmitEmptyCollectionConverter<T[], T, ArrayConverter<T, TConverter>>>> attribute, T element)
        where TConverter : IDynamoDbConverter<T> => new(attribute.Path, element);

    /// <summary>Checks with <c>contains</c> whether a string holds a substring, compared by bytes, so case matters. An attribute that does not exist holds nothing, so the condition fails there and its <c>Negate()</c> holds.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="substring">The substring.</param>
    /// <returns>The condition.</returns>
    public static Contains<string, StringConverter> Contains(this Attr<string, StringConverter> attribute, string substring) => new(attribute.Path, substring);

    /// <summary>Checks with <c>contains</c> whether the string of a property with <see cref="OmitEmptyAttribute"/> holds a substring. See <see cref="Contains(Attr{string, StringConverter}, string)"/>.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="substring">The substring.</param>
    /// <returns>The condition.</returns>
    public static Contains<string, StringConverter> Contains(this Attr<string, OmitEmptyConverter<StringConverter>> attribute, string substring) => new(attribute.Path, substring);

    /// <summary>Checks with <c>contains</c> whether the string of a nullable property holds a substring. See <see cref="Contains(Attr{string, StringConverter}, string)"/>.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="substring">The substring.</param>
    /// <returns>The condition.</returns>
    public static Contains<string, StringConverter> Contains(this Attr<string, NullableReferenceHandleConverter<string, StringConverter>> attribute, string substring) => new(attribute.Path, substring);

    /// <summary>Checks with <c>contains</c> whether the string of a nullable property with <see cref="OmitEmptyAttribute"/> holds a substring. See <see cref="Contains(Attr{string, StringConverter}, string)"/>.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="substring">The substring.</param>
    /// <returns>The condition.</returns>
    public static Contains<string, StringConverter> Contains(this Attr<string, NullableReferenceHandleConverter<string, OmitEmptyConverter<StringConverter>>> attribute, string substring) => new(attribute.Path, substring);

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
