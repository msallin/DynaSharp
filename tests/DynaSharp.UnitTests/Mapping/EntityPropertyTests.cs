using DynaSharp.UnitTests.Properties;
using FsCheck;
using FsCheck.Fluent;
using TUnit.FsCheck;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>
/// The code the generator writes for an entity of value types, over any values of its properties: what it writes, it reads back as an equal entity, and an enum value that has
/// no name, which it stores by name, is refused on the write with a <see cref="DynamoDbMappingException"/>. A <c>[Flags]</c> enum is stored as a number and takes any value.
/// </summary>
public sealed class EntityPropertyTests
{
    [Test]
    [FsCheckProperty(MaxTest = PropertyRun.Cases, Replay = PropertyRun.Seed, QuietOnSuccess = true, Arbitrary = [typeof(Arbitraries), typeof(Fields)])]
    public async Task RoundTrip_AnyReading_GivesAnEqualReadingOrRefusesAnEnumValueWithoutAName(ReadingFields fields)
    {
        // Arrange
        var reading = fields.ToReading();
        var named = Enum.IsDefined(reading.Status) && (reading.Previous is null || Enum.IsDefined(reading.Previous.Value));

        // Act
        var outcome = RoundTrip(reading);

        // Assert
        await Assert.That(outcome).IsEqualTo(named ? "equal" : nameof(DynamoDbMappingException));
    }

    private static string RoundTrip(Reading reading)
    {
        try
        {
            var back = EntityJson.Read<Reading>(EntityJson.Write(reading));
            return back.Equals(reading) ? "equal" : $"read back as {EntityJson.Write(back)}";
        }
        catch (DynamoDbMappingException)
        {
            return nameof(DynamoDbMappingException);
        }
    }

    /// <summary>The properties of a <see cref="Reading"/>, which is internal, as a type a property can take.</summary>
    /// <param name="Sensor">The partition key.</param>
    /// <param name="Value">A double.</param>
    /// <param name="Calibrated">A decimal.</param>
    /// <param name="Valid">A boolean.</param>
    /// <param name="Run">A Guid.</param>
    /// <param name="At">An instant.</param>
    /// <param name="Day">A day.</param>
    /// <param name="Duration">A span.</param>
    /// <param name="Status">An enum stored by name, possibly a value without a name.</param>
    /// <param name="Rights">A flags enum stored as a number, of any bits.</param>
    /// <param name="Offset">A nullable number.</param>
    /// <param name="Previous">A nullable enum stored by name.</param>
    public sealed record ReadingFields(long Sensor, double Value, decimal Calibrated, bool Valid, Guid Run, DateTimeOffset At, DateOnly Day, TimeSpan Duration, int Status, byte Rights, int? Offset, int? Previous)
    {
        internal Reading ToReading() => new()
        {
            Sensor = Sensor,
            Value = Value,
            Calibrated = Calibrated,
            Valid = Valid,
            Run = Run,
            At = At,
            Day = Day,
            Duration = Duration,
            Status = (Status)Status,
            Rights = (Access)Rights,
            Offset = Offset,
            Previous = (Status?)Previous,
        };
    }

    internal static class Fields
    {
        public static Arbitrary<ReadingFields> Readings()
        {
            // Mostly the members, Settled among them, which is another name of Paid; now and then a value without a name.
            var status = Gen.Frequency((6, Gen.Elements(0, 1, 2)), (1, Gen.Elements(-1, 3, 255)));
            return (
                from sensor in Generate.Int64
                from value in Generate.Double
                from calibrated in Generate.Decimal
                from valid in Gen.Elements(false, true)
                from run in Generate.Guid
                from at in Generate.DateTimeOffset
                from day in Generate.DateOnly
                from duration in Generate.TimeSpan
                from current in status
                from rights in Gen.Choose(0, 255).Select(static bits => (byte)bits)
                from offset in Gen.Frequency((1, Gen.Constant<int?>(null)), (3, Generate.Int64.Select(static number => (int?)(int)number)))
                from previous in Gen.Frequency((1, Gen.Constant<int?>(null)), (3, status.Select(static member => (int?)member)))
                select new ReadingFields(sensor, value, calibrated, valid, run, at, day, duration, current, rights, offset, previous)).ToArbitrary();
        }
    }
}
