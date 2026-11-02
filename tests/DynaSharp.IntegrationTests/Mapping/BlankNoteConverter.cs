using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Mapping;

/// <summary>Counts a blank string as missing, which is how a service stores a note that is not there.</summary>
internal readonly record struct BlankNoteConverter : IDynamoDbConverter<CabinNote>
{
    public static void Write(DynamoDbWriter writer, CabinNote value) => writer.WriteString(value.Text);

    public static CabinNote Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return new CabinNote(reader.GetString());
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type == AttributeType.String && reader.GetStringUtf8().IsEmpty;
}
