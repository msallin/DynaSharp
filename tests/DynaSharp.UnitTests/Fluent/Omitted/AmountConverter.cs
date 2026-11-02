using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>Leaves an amount of 0 out.</summary>
internal readonly struct AmountConverter : IDynamoDbConverter<Amount>
{
    public static void Write(DynamoDbWriter writer, Amount value) => writer.WriteNumber(value.Cents);

    public static Amount Read(ref DynamoDbReader reader, AttributeType type) => new(NumberConverter<int>.Read(ref reader, type));

    public static bool IsOmitted(Amount value) => value.Cents == 0;
}
