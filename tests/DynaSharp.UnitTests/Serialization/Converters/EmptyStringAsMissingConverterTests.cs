using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// The wrapper alone, and through <see cref="Remittance"/>, whose properties name it in <c>[DynamoDbConverter]</c>. Handles sit in variables where a <c>Select</c> throws,
/// because <c>DYNA209</c> reports it where it sees a handle.
/// </summary>
public sealed class EmptyStringAsMissingConverterTests
{
    private const string Shipped = """{"Item":{"_type":{"S":"REMITTANCE"},"Id":{"S":"r1"},"PaidAt":{"S":"2026-10-31T12:00:00.000Z"},"ShippedAt":{"S":"2026-10-31T12:00:00.0000000Z"},"DueAt":{"S":"2026-11-30T12:00:00.0000000+01:00"},"Position":{"S":"Bow"},"After":{"N":"7"}}}""";

    private const string Blank = """{"Item":{"_type":{"S":"REMITTANCE"},"Id":{"S":"r1"},"PaidAt":{"S":"2026-10-31T12:00:00.000Z"},"ShippedAt":{"S":""},"DueAt":{"S":""},"Position":{"S":""},"After":{"N":"7"}}}""";

    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Write_Value_WritesWhatTheConverterWrites()
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<DateTimeOffset, EmptyStringAsMissingConverter<DateTimeOffset, DateTimeOffsetConverter>>(Noon);

        // Assert
        await Assert.That(json).IsEqualTo(ConverterRun.Write<DateTimeOffset, DateTimeOffsetConverter>(Noon));
    }

    [Test]
    public async Task Read_Value_ReadsWhatTheConverterReads()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<DateTimeOffset, EmptyStringAsMissingConverter<DateTimeOffset, DateTimeOffsetConverter>>("""{"S":"2026-10-31T14:00:00+02:00"}""");

        // Assert
        await Assert.That(value).IsEqualTo(Noon);
    }

    [Test]
    public async Task Read_EmptyString_StaysAsStrictAsTheConverter()
    {
        // Arrange (a list has positions and asks nothing, so Read cannot skip a value)
        // Act
        var act = () => ConverterRun.Read<DateTimeOffset, EmptyStringAsMissingConverter<DateTimeOffset, DateTimeOffsetConverter>>("""{"S":""}""");

        // Assert
        await Assert.That(act).ThrowsExactly<FormatException>();
    }

    [Test]
    [Arguments("""{"S":""}""", true)]
    [Arguments("""{"S":" "}""", false)]
    [Arguments("""{"S":"x"}""", false)]
    [Arguments("""{"N":"0"}""", false)]
    [Arguments("""{"SS":[""]}""", false)]
    [Arguments("""{"L":[{"S":""}]}""", false)]
    [Arguments("""{"NULL":true}""", false)]
    public async Task CountsAsMissing_Value_IsTrueForAnEmptyStringAlone(string json, bool expected)
    {
        // Arrange
        // Act
        var counted = CountsAsMissing<string, EmptyStringAsMissingConverter<string, StringConverter>>(json);

        // Assert
        await Assert.That(counted).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"S":"TELEPORTED"}""", true)]
    [Arguments("""{"S":"Bow"}""", false)]
    public async Task CountsAsMissing_ValueTheConverterCounts_PassesItsAnswerOn(string json, bool expected)
    {
        // Arrange
        // Act
        var counted = CountsAsMissing<Position, EmptyStringAsMissingConverter<Position, LenientPositionConverter>>(json);

        // Assert
        await Assert.That(counted).IsEqualTo(expected);
    }

    [Test]
    public async Task IsOmitted_ValueTheConverterOmits_PassesItsAnswerOn()
    {
        // Arrange
        // Act
        var omitted = (IsOmitted<string, EmptyStringAsMissingConverter<string, OmitEmptyConverter<StringConverter>>>(string.Empty), IsOmitted<string, EmptyStringAsMissingConverter<string, StringConverter>>(string.Empty));

        // Assert
        await Assert.That(omitted).IsEqualTo((true, false));
    }

    [Test]
    public async Task TryReadMissing_ConverterWithAnAnswer_HasNoneOfItsOwn()
    {
        // Arrange (an answer would tell DYNA209 that a Select of the property cannot throw)
        // Act
        var answered = (TryReadMissing<string[], SetArrayConverter<string, StringSetElement>>(), TryReadMissing<string[], EmptyStringAsMissingConverter<string[], SetArrayConverter<string, StringSetElement>>>());

        // Assert
        await Assert.That(answered).IsEqualTo((true, false));
    }

    [Test]
    public async Task ItemAsync_EmptyStrings_ReadAsMissingAttributes()
    {
        // Arrange (the nullable reads null, the others keep their initial values, and the next attribute reads)
        using var handler = new StubHandler(static _ => StubResponse.Json(Blank));
        using var client = TestClient.Create(handler);

        // Act
        var remittance = await client.Table<Remittance>("Shop").Get(Remittance.Keys.Of("r1", Noon.UtcDateTime)).ItemAsync();

        // Assert
        await Assert.That((remittance!.ShippedAt, remittance.DueAt, remittance.Position, remittance.After)).IsEqualTo(((DateTimeOffset?)null, Remittance.Initial, Position.Unknown, 7));
    }

    [Test]
    public async Task ItemAsync_Values_ReadThroughTheConverters()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json(Shipped));
        using var client = TestClient.Create(handler);

        // Act
        var remittance = await client.Table<Remittance>("Shop").Get(Remittance.Keys.Of("r1", Noon.UtcDateTime)).ItemAsync();

        // Assert
        await Assert.That((remittance!.ShippedAt, remittance.DueAt.Offset, remittance.Position)).IsEqualTo(((DateTimeOffset?)Noon, TimeSpan.FromHours(1), Position.Bow));
    }

    [Test]
    public async Task ItemAsync_NameTheConverterCounts_ReadsAsAMissingAttribute()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json(Shipped.Replace("\"Bow\"", "\"TELEPORTED\"", StringComparison.Ordinal)));
        using var client = TestClient.Create(handler);

        // Act
        var remittance = await client.Table<Remittance>("Shop").Get(Remittance.Keys.Of("r1", Noon.UtcDateTime)).ItemAsync();

        // Assert
        await Assert.That(remittance!.Position).IsEqualTo(Position.Unknown);
    }

    [Test]
    public async Task SendAsync_SelectOfAnEmptyStringOnANullableProperty_IsFoundWithNull()
    {
        // Arrange
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"ShippedAt":{"S":""}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var shipped = await client.Table<Remittance>("Shop").Get(Remittance.Keys.Of("r1", Noon.UtcDateTime)).Select(Remittance.Attrs.ShippedAt).SendAsync();

        // Assert
        await Assert.That((shipped.Found, shipped.Value)).IsEqualTo((true, (DateTimeOffset?)null));
    }

    [Test]
    public async Task SendAsync_SelectOfAnEmptyStringOnAPropertyThatCannotBeNull_Throws()
    {
        // Arrange (a Select has no initial value to keep)
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"DueAt":{"S":""}}}"""));
        using var client = TestClient.Create(handler);
        var due = Remittance.Attrs.DueAt;

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(async () => await client.Table<Remittance>("Shop").Get(Remittance.Keys.Of("r1", Noon.UtcDateTime)).Select(due).SendAsync());

        // Assert
        await Assert.That(failure.AttributeName).IsEqualTo("DueAt");
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEntity_WritesWhatTheConvertersWrite()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var remittance = new Remittance { Id = "r1", PaidAt = Noon.UtcDateTime, ShippedAt = Noon, Position = Position.Stern };

        // Act
        var json = client.Table<Remittance>("Shop").Put(remittance).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ShippedAt":{"S":"2026-10-31T12:00:00.0000000Z"},"DueAt":{"S":"2000-01-01T00:00:00.0000000+00:00"},"Position":{"S":"Stern"}
            """);
    }

    private static bool CountsAsMissing<T, TConverter>(string json)
        where TConverter : IDynamoDbConverter<T> =>
        TestJson.Read(json, static (ref reader) =>
        {
            var type = reader.ReadStartAttribute();
            return TConverter.CountsAsMissing(in reader, type);
        });

    private static bool IsOmitted<T, TConverter>(T value)
        where TConverter : IDynamoDbConverter<T> => TConverter.IsOmitted(value);

    private static bool TryReadMissing<T, TConverter>()
        where TConverter : IDynamoDbConverter<T> => TConverter.TryReadMissing(out _);
}
