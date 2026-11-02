using System.Globalization;
using DynaSharp.Serialization;

namespace DynaSharp.DefaultsFixture;

/// <summary>Stores a timestamp as what a service wrote before it used this library: milliseconds since the epoch, as text.</summary>
public readonly record struct StoredTimestampConverter : IDynamoDbConverter<DateTimeOffset>
{
    public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteString(value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

    public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(reader.GetString(), CultureInfo.InvariantCulture));
    }
}
