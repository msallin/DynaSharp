using FsCheck;
using FsCheck.Fluent;

namespace DynaSharp.UnitTests.Properties;

/// <summary>
/// The generators of <see cref="Generate"/> as FsCheck takes them for the parameters of a property: <c>[FsCheckProperty(Arbitrary = [typeof(Arbitraries)])]</c>.
/// They replace the defaults of the types they name, so a <see cref="string"/> parameter gets text of every plane and a <see cref="long"/> one every magnitude.
/// </summary>
internal static class Arbitraries
{
    public static Arbitrary<string> Text() => Arb.From(Generate.Text, Generate.ShrinkText);

    public static Arbitrary<LoneSurrogateText> LoneSurrogate() => Generate.TextWithLoneSurrogate.Select(static text => new LoneSurrogateText(text)).ToArbitrary();

    public static Arbitrary<int> Int32() => Generate.Int64.Select(static value => (int)value).ToArbitrary();

    public static Arbitrary<long> Int64() => Generate.Int64.ToArbitrary();

    public static Arbitrary<ulong> UInt64() => Generate.UInt64.ToArbitrary();

    public static Arbitrary<decimal> Decimal() => Generate.Decimal.ToArbitrary();

    public static Arbitrary<double> Double() => Generate.Double.ToArbitrary();

    public static Arbitrary<float> Single() => Generate.Single.ToArbitrary();

    public static Arbitrary<DateTime> DateTime() => Generate.UtcDateTime.ToArbitrary();

    public static Arbitrary<DateTimeOffset> DateTimeOffset() => Generate.DateTimeOffset.ToArbitrary();

    public static Arbitrary<TimeSpan> TimeSpan() => Generate.TimeSpan.ToArbitrary();

    public static Arbitrary<DateOnly> DateOnly() => Generate.DateOnly.ToArbitrary();

    public static Arbitrary<TimeOnly> TimeOnly() => Generate.TimeOnly.ToArbitrary();

    public static Arbitrary<Guid> Guid() => Generate.Guid.ToArbitrary();

    public static Arbitrary<byte[]> Bytes() => Generate.Bytes.ToArbitrary();

    public static Arbitrary<AttributeValue> AttributeValue() => Arb.From(Generate.AttributeValue, Generate.ShrinkAttributeValue);

    public static Arbitrary<JsonShapedValue> JsonShaped() => Generate.JsonShapedValue.Select(static value => new JsonShapedValue(value)).ToArbitrary();
}
