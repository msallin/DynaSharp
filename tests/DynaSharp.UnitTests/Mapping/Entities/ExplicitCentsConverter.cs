using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Stores <see cref="Cents"/> as a number, through members that cannot be called on the type.</summary>
internal readonly struct ExplicitCentsConverter : IDynamoDbConverter<Cents>
{
    static void IDynamoDbConverter<Cents>.Write(DynamoDbWriter writer, Cents value) => writer.WriteNumber(value.Value);

    static Cents IDynamoDbConverter<Cents>.Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Number);
        return new Cents(reader.GetNumber<long>());
    }
}
