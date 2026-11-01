using System.Numerics;

namespace DynaSharp.Serialization;

/// <summary>The elements of a number set (<c>NS</c>): any .NET number type, formatted and parsed as <see cref="NumberConverter{T}"/> does.</summary>
/// <typeparam name="T">The number type.</typeparam>
/// <remarks>
/// DynamoDB rejects a number set whose elements are numerically equal, so <c>1</c> and <c>1.0</c> cannot both be in one. A
/// <see cref="HashSet{T}"/> of <see cref="decimal"/> holds <c>1m</c> and <c>1.0m</c> as one element, since they are equal.
/// </remarks>
public readonly struct NumberSetElement<T> : IDynamoDbSetElementConverter<T>
    where T : INumberBase<T>
{
    /// <inheritdoc/>
    public static AttributeType SetType => AttributeType.NumberSet;

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite; DynamoDB cannot store either.</exception>
    public static void WriteElement(DynamoDbWriter writer, T value) => writer.WriteNumberSetElement(value);

    /// <inheritdoc/>
    /// <exception cref="OverflowException">The number does not fit <typeparamref name="T"/>, see <see cref="NumberConverter{T}.Read"/>.</exception>
    public static T ReadElement(ref DynamoDbReader reader) => NumberConverter<T>.ReadNumber(ref reader);
}
