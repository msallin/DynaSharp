using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// Looks inside a map: <c>{"M":{"sender":{"S":"a"},"body":{"S":""}}}</c> counts as missing, because its body is blank. The parameter cannot be moved, so the walk goes over a copy, and <see cref="Read"/> finds the map untouched.
/// </summary>
internal readonly struct EnvelopeConverter : IDynamoDbConverter<Envelope>
{
    public static void Write(DynamoDbWriter writer, Envelope value) => throw new NotSupportedException("Only read.");

    public static Envelope Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Map);
        var (sender, body) = (string.Empty, string.Empty);
        while (reader.TryReadAttributeName())
        {
            var isSender = reader.AttributeNameEquals("sender"u8);
            var text = reader.ReadAttribute<string, StringConverter>();
            (sender, body) = isSender ? (text, body) : (sender, text);
        }

        return new Envelope(sender, body);
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type)
    {
        if (type != AttributeType.Map)
        {
            return false;
        }

        var map = reader;
        while (map.TryReadAttributeName())
        {
            if (map.AttributeNameEquals("body"u8))
            {
                return map.ReadStartAttribute() == AttributeType.String && map.GetStringUtf8().IsEmpty;
            }

            map.SkipAttributeValue();
        }

        return true;
    }
}
