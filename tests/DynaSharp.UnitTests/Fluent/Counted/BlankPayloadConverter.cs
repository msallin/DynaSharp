using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Counts a blank string as missing, which is how a service stores a payload that is not there.</summary>
internal readonly struct BlankPayloadConverter : IDynamoDbConverter<Payload>
{
    public static void Write(DynamoDbWriter writer, Payload value) => writer.WriteString(value.Text);

    public static Payload Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return new Payload(reader.GetString());
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type == AttributeType.String && reader.GetStringUtf8().IsEmpty;
}
