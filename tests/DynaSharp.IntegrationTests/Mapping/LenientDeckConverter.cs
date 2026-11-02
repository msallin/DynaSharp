using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>Counts a name the enum does not have as missing.</summary>
internal readonly record struct LenientDeckConverter : IDynamoDbConverter<Deck>
{
    public static void Write(DynamoDbWriter writer, Deck value) => writer.WriteString(value.ToString());

    public static Deck Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return Enum.TryParse<Deck>(reader.GetString(), out var value) ? value : throw new FormatException("Not a deck.");
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type == AttributeType.String && !Enum.TryParse<Deck>(reader.GetString(), out _);
}
