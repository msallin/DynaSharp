namespace DynaSharp.Serialization;

/// <summary>
/// Converts the elements of a DynamoDB set. The elements of a set have no type descriptor of their own: the set as a whole is a string set
/// (<c>SS</c>), number set (<c>NS</c>) or binary set (<c>BS</c>), and each element is a plain JSON string.
/// </summary>
/// <typeparam name="T">The .NET type of the elements.</typeparam>
/// <remarks>
/// Pass an implementation to <see cref="HashSetConverter{T, TElement}"/>, <see cref="ReadOnlySetConverter{T, TElement}"/> or
/// <see cref="SetArrayConverter{T, TElement}"/>. The built-in ones are <see cref="StringSetElement"/>, <see cref="NumberSetElement{T}"/>,
/// <see cref="BinarySetElement"/> and <see cref="GuidSetElement"/>. Implement it on a struct, for example to store an enumeration by name in a
/// string set.
/// </remarks>
public interface IDynamoDbSetElementConverter<T>
{
    /// <summary>Gets the kind of set the elements belong to: <see cref="AttributeType.StringSet"/>, <see cref="AttributeType.NumberSet"/> or <see cref="AttributeType.BinarySet"/>.</summary>
    public static abstract AttributeType SetType { get; }

    /// <summary>Writes one element, with the writer's <c>Write...SetElement</c> method for <see cref="SetType"/>.</summary>
    /// <param name="writer">The writer, inside a set that <see cref="SetType"/> started.</param>
    /// <param name="value">The element.</param>
    public static abstract void WriteElement(DynamoDbWriter writer, T value);

    /// <summary>Reads the element the reader is positioned on, with the reader's getter for <see cref="SetType"/>. Reads nothing beyond the element.</summary>
    /// <param name="reader">The reader, positioned on an element after <see cref="DynamoDbReader.TryReadSetElement"/> returned <see langword="true"/>.</param>
    /// <returns>The element.</returns>
    public static abstract T ReadElement(ref DynamoDbReader reader);
}
