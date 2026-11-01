using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// Reads an item the way the generated code does, with a length switch over <c>GetAttributeNameUtf8</c> and <c>ReadAttribute</c>, and proves
/// that attributes of known names and value kinds that need no new object cost no allocation.
/// </summary>
public sealed class KnownNameReadTests
{
    private const string Json = """
        {"pk":{"S":"USER#1"},"Count":{"N":"7"},"Price":{"N":"12.50"},"Active":{"BOOL":true},
         "Id":{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"},"At":{"S":"2026-10-31T12:00:00.0000000Z"},
         "Day":{"S":"2026-10-31"},"Clock":{"S":"12:34:56.7890123"},"Span":{"S":"1.02:03:04.5000000"},
         "ttl":{"N":"1793448000"},"Maybe":{"NULL":true},"Extra":{"M":{"x":{"L":[{"S":"y"},{"N":"1"}]}}},"_type":{"S":"ORDER"}}
        """;

    private static readonly byte[] Utf8 = System.Text.Encoding.UTF8.GetBytes(Json);

    [Test]
    public async Task ReadAttribute_KnownNames_ReturnsEveryValue()
    {
        // Arrange
        // Act
        var item = ReadItem(Utf8);

        // Assert
        await Assert.That(item).IsEqualTo(new Sample(
            7,
            12.5m,
            true,
            new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
            new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 10, 31),
            new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(7890123)),
            new TimeSpan(1, 2, 3, 4, 500),
            new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
            null));
    }

    [Test]
    public async Task ReadAttribute_KnownNamesAndSkippedAttributes_AllocateNothing()
    {
        // Arrange (a first pass compiles everything the second pass measures)
        for (var i = 0; i < 5; i++)
        {
            ReadItem(Utf8);
        }

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            ReadItem(Utf8);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    private static Sample ReadItem(byte[] json)
    {
        var reader = new DynamoDbReader(json);
        var sample = default(Sample);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var name = reader.GetAttributeNameUtf8();
            switch (name.Length)
            {
                case 2:
                    if (name.SequenceEqual("Id"u8))
                    {
                        sample = sample with { Id = reader.ReadAttribute<Guid, GuidConverter>() };
                        continue;
                    }

                    if (name.SequenceEqual("At"u8))
                    {
                        sample = sample with { At = reader.ReadAttribute<DateTimeOffset, DateTimeOffsetConverter>() };
                        continue;
                    }

                    break;
                case 3:
                    if (name.SequenceEqual("Day"u8))
                    {
                        sample = sample with { Day = reader.ReadAttribute<DateOnly, DateOnlyConverter>() };
                        continue;
                    }

                    if (name.SequenceEqual("ttl"u8))
                    {
                        sample = sample with { Ttl = reader.ReadAttribute<DateTimeOffset, EpochSecondsConverter>() };
                        continue;
                    }

                    break;
                case 4:
                    if (name.SequenceEqual("Span"u8))
                    {
                        sample = sample with { Span = reader.ReadAttribute<TimeSpan, TimeSpanConverter>() };
                        continue;
                    }

                    break;
                case 5:
                    if (name.SequenceEqual("Count"u8))
                    {
                        sample = sample with { Count = reader.ReadAttribute<int, NumberConverter<int>>() };
                        continue;
                    }

                    if (name.SequenceEqual("Price"u8))
                    {
                        sample = sample with { Price = reader.ReadAttribute<decimal, NumberConverter<decimal>>() };
                        continue;
                    }

                    if (name.SequenceEqual("Clock"u8))
                    {
                        sample = sample with { Clock = reader.ReadAttribute<TimeOnly, TimeOnlyConverter>() };
                        continue;
                    }

                    if (name.SequenceEqual("Maybe"u8))
                    {
                        sample = sample with { Maybe = reader.ReadAttribute<int?, NullableConverter<int, NumberConverter<int>>>() };
                        continue;
                    }

                    break;
                case 6:
                    if (name.SequenceEqual("Active"u8))
                    {
                        sample = sample with { Active = reader.ReadAttribute<bool, BoolConverter>() };
                        continue;
                    }

                    break;
            }

            // pk, _type, Extra and anything else unknown: no string, no value parsed.
            reader.SkipAttributeValue();
        }

        return sample;
    }

    private readonly record struct Sample(
        int Count,
        decimal Price,
        bool Active,
        Guid Id,
        DateTimeOffset At,
        DateOnly Day,
        TimeOnly Clock,
        TimeSpan Span,
        DateTimeOffset Ttl,
        int? Maybe);
}
