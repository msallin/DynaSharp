using System.Numerics;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts any .NET number type to and from a number value (<c>N</c>): integers, <see cref="decimal"/>, <see cref="double"/>,
/// <see cref="float"/>, <see cref="Half"/>, <see cref="Int128"/>, <see cref="UInt128"/> and <see cref="BigInteger"/>.
/// </summary>
/// <typeparam name="T">The number type.</typeparam>
/// <remarks>
/// <para>
/// Numbers travel as invariant text in the shortest form that round-trips, so a <see cref="decimal"/> keeps every digit and its scale
/// (<c>12.50</c> is written as <c>12.50</c>). DynamoDB normalizes what it stores, <c>12.50</c> comes back as <c>12.5</c>, so compare numbers
/// by value, not by text. DynamoDB keeps at most 38 significant digits and rejects magnitudes outside 1E-130 to 9.99E+125 with a validation
/// error: a <see cref="double"/> or a <see cref="BigInteger"/> can be outside that range, and the converter does not check.
/// </para>
/// <para>
/// Reading parses the text of the value in place and allocates nothing. Exponent notation is accepted, also for integer types, as long as
/// the value is integral (<c>1E+2</c> reads as 100).
/// </para>
/// <para>
/// A <see cref="char"/> counts as a number type in .NET but not here: it is stored as a string value of one character, and read back from one.
/// </para>
/// </remarks>
public readonly struct NumberConverter<T> : IDynamoDbConverter<T>, IAddableConverter
    where T : INumberBase<T>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite; DynamoDB cannot store either.</exception>
    public static void Write(DynamoDbWriter writer, T value) => writer.WriteNumber(value);

    /// <inheritdoc/>
    /// <exception cref="JsonException"><paramref name="type"/> is not <see cref="AttributeType.Number"/>.</exception>
    /// <exception cref="OverflowException">
    /// The number does not fit <typeparamref name="T"/>: it is out of range, or has a fractional part and <typeparamref name="T"/> is an integer
    /// type. A floating-point type counts a number that rounds to infinity as out of range. The message does not quote the number.
    /// </exception>
    public static T Read(ref DynamoDbReader reader, AttributeType type)
    {
        if (typeof(T) == typeof(char))
        {
            return ReadCharacter(ref reader, type);
        }

        ConverterChecks.Expect(type, AttributeType.Number);
        return ReadNumber(ref reader);
    }

    /// <summary>Reads the number at the reader's position, for the converters of number sets, which have no type descriptor.</summary>
    /// <param name="reader">The reader, positioned on a number.</param>
    /// <returns>The number.</returns>
    internal static T ReadNumber(ref DynamoDbReader reader)
    {
        if (!reader.TryGetNumber<T>(out var value) || !T.IsFinite(value))
        {
            throw new OverflowException("The number is malformed or does not fit the target type.");
        }

        return value;
    }

    private static T ReadCharacter(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        var text = reader.GetString();
        return text.Length == 1 ? T.CreateTruncating(text[0]) : throw new JsonException("Expected a string of one character.");
    }
}
