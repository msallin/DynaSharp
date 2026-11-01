using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ConverterChecksTests
{
    [Test]
    [Arguments(AttributeType.String)]
    [Arguments(AttributeType.Number)]
    [Arguments(AttributeType.Binary)]
    [Arguments(AttributeType.Boolean)]
    [Arguments(AttributeType.Null)]
    [Arguments(AttributeType.List)]
    [Arguments(AttributeType.Map)]
    [Arguments(AttributeType.StringSet)]
    [Arguments(AttributeType.NumberSet)]
    [Arguments(AttributeType.BinarySet)]
    public async Task Expect_SameType_DoesNotThrow(AttributeType type)
    {
        // Arrange
        // Act
        var act = () => ConverterChecks.Expect(type, type);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Expect_OtherType_ThrowsJsonExceptionNamingBothTypes()
    {
        // Arrange
        // Act
        var act = () => ConverterChecks.Expect(AttributeType.Null, AttributeType.Number);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected a DynamoDB attribute value of type Number but found Null.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Expect_NoneFoundWhereASetIsExpected_ThrowsJsonException()
    {
        // Arrange
        // Act
        var act = () => ConverterChecks.Expect(AttributeType.None, AttributeType.StringSet);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("Expected a DynamoDB attribute value of type StringSet but found None.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ExpectDiscriminator_SameValue_ReadsPastTheAttribute()
    {
        // Arrange
        // Act
        var next = Check("""{"_type":{"S":"ORDER"},"next":{"N":"1"}}""", "ORDER"u8.ToArray());

        // Assert
        await Assert.That(next).IsEqualTo("next");
    }

    [Test]
    public async Task ExpectDiscriminator_OtherValue_NamesTheTypeFound()
    {
        // Arrange
        // Act
        var act = () => Check("""{"_type":{"S":"INVOICE"}}""", "ORDER"u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<DynamoDbMappingException>()
            .WithMessage("The item is of type \"INVOICE\", so it cannot be read as Order.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ExpectDiscriminator_NotAString_ThrowsJsonException()
    {
        // Arrange
        // Act
        var act = () => Check("""{"_type":{"N":"1"}}""", "ORDER"u8.ToArray());

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
    }

    private static string Check(string json, byte[] expected) => TestJson.Read(json, (ref reader) =>
    {
        reader.ReadStartItem();
        reader.TryReadAttributeName();
        ConverterChecks.ExpectDiscriminator(ref reader, expected, "Order", "_type");
        reader.TryReadAttributeName();
        return reader.GetAttributeName();
    });
}
