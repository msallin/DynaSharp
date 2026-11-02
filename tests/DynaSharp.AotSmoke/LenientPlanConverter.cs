using DynaSharp.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>Counts a name that <see cref="Plan"/> does not have as missing.</summary>
internal readonly struct LenientPlanConverter : IDynamoDbConverter<Plan>
{
    public static void Write(DynamoDbWriter writer, Plan value) => writer.WriteString(value.ToString());

    public static Plan Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return Enum.TryParse<Plan>(reader.GetString(), out var value) ? value : throw new FormatException("Not a plan.");
    }

    public static bool CountsAsMissing(in DynamoDbReader reader, AttributeType type) => type == AttributeType.String && !Enum.TryParse<Plan>(reader.GetString(), out _);
}
