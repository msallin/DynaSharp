using System.Text.Json;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Serialization;

public sealed class BoolAsNumberConverterTests
{
    public static IEnumerable<(string Json, AttributeType Found)> NotNumbers() => AttributeSamples.Except(AttributeType.Number);

    [Test]
    [Arguments(true, """{"N":"1"}""")]
    [Arguments(false, """{"N":"0"}""")]
    public async Task Write_Value_WritesOneOrZero(bool value, string expected)
    {
        // Arrange
        // Act
        var json = ConverterRun.Write<bool, BoolAsNumberConverter>(value);

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"N":"1"}""", true)]
    [Arguments("""{"N":"0"}""", false)]
    [Arguments("""{"N":"-0"}""", false)]
    [Arguments("""{"N":"0.000"}""", false)]
    [Arguments("""{"N":"0E+5"}""", false)]
    [Arguments("""{"N":"2"}""", true)]
    [Arguments("""{"N":"-1"}""", true)]
    [Arguments("""{"N":"0.5"}""", true)]
    [Arguments("""{"N":"1E-130"}""", true)]
    [Arguments("""{"N":"9.9999999999999999999999999999999999999E+125"}""", true)]
    public async Task Read_Number_IsFalseForZeroAndTrueForAnyOther(string json, bool expected)
    {
        // Arrange (the AWS SDK reads every number but zero as true, and DynamoDB returns zero as 0)
        // Act
        var value = ConverterRun.Read<bool, BoolAsNumberConverter>(json);

        // Assert
        await Assert.That(value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("""{"N":"abc"}""")]
    [Arguments("""{"N":"1E+999"}""")]
    [Arguments("""{"N":""}""")]
    public async Task Read_TextThatIsNoNumberOfTheRange_ThrowsOverflowExceptionWithoutTheText(string json)
    {
        // Arrange
        // Act
        var act = () => ConverterRun.Read<bool, BoolAsNumberConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>()
            .WithMessage("The number is malformed or does not fit the target type.", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(NotNumbers))]
    public async Task Read_OtherType_ThrowsJsonException(string json, AttributeType found)
    {
        // Arrange (a BOOL, which the built-in converter writes, is one of them)
        // Act
        var act = () => ConverterRun.Read<bool, BoolAsNumberConverter>(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage(AttributeSamples.WrongTypeMessage(AttributeType.Number, found), StringComparison.Ordinal);
    }

    [Test]
    public async Task Read_NullableOfTheConverter_ReadsNullForTheNullValue()
    {
        // Arrange
        // Act
        var value = ConverterRun.Read<bool?, NullableConverter<bool, BoolAsNumberConverter>>("""{"NULL":true}""");

        // Assert
        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task ToRequestJson_PutOfAnEntity_StoresTheBooleansAsNumbers()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var remittance = new Remittance { Id = "r1", PaidAt = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc), Settled = true, Disputed = false };

        // Act
        var json = client.Table<Remittance>("Shop").Put(remittance).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "Settled":{"N":"1"},"Disputed":{"N":"0"}
            """);
    }

    [Test]
    public async Task ItemAsync_EntityWithNumbers_ReadsTheBooleans()
    {
        // Arrange (an item the AWS SDK wrote in its conversion V1)
        using var handler = new StubHandler(static _ => StubResponse.Json("""{"Item":{"_type":{"S":"REMITTANCE"},"Id":{"S":"r1"},"PaidAt":{"S":"2026-10-31T12:00:00.000Z"},"Settled":{"N":"1"},"Disputed":{"N":"0"}}}"""));
        using var client = TestClient.Create(handler);

        // Act
        var remittance = await client.Table<Remittance>("Shop").Get(Remittance.Keys.Of("r1", new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc))).ItemAsync();

        // Assert
        await Assert.That((remittance!.Settled, remittance.Disputed)).IsEqualTo((true, (bool?)false));
    }
}
