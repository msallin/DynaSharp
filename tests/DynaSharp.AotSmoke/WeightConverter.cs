using System.Globalization;
using DynaSharp.Serialization;

namespace DynaSharp.AotSmoke;

/// <summary>Stores a <see cref="Weight"/> as text.</summary>
internal readonly struct WeightConverter : IDynamoDbConverter<Weight>
{
    public static void Write(DynamoDbWriter writer, Weight value) => writer.WriteString(value.Grams.ToString(CultureInfo.InvariantCulture) + "g");

    public static Weight Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return new Weight(int.Parse(reader.GetString().TrimEnd('g'), CultureInfo.InvariantCulture));
    }
}
