using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>The custom converter example from the <see cref="IDynamoDbConverter{T}"/> documentation, verbatim but for its accessibility.</summary>
internal readonly struct UriConverter : IDynamoDbConverter<Uri>
{
    public static void Write(DynamoDbWriter writer, Uri value) =>
        writer.WriteString(value.OriginalString);

    public static Uri Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return new Uri(reader.GetString(), UriKind.Absolute);
    }
}
