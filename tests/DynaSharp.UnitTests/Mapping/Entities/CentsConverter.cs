using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Stores a <see cref="Cents"/> as text, so that a test sees the converter ran.</summary>
internal readonly struct CentsConverter : IDynamoDbConverter<Cents>
{
    public static void Write(DynamoDbWriter writer, Cents value) => writer.WriteString(FormattableString.Invariant($"{value.Value}c"));

    public static Cents Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return new Cents(long.Parse(reader.GetString().TrimEnd('c'), System.Globalization.CultureInfo.InvariantCulture));
    }
}
