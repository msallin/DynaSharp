using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class RequestJsonTests
{
    [Test]
    public async Task Members_ArrayInsteadOfObject_ThrowsSerialization()
    {
        // Arrange
        var value = JsonElement.Parse("[]");

        // Act
        var act = () => RequestJson.Members(value, "GetItem", "TableName");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The member GetItem must be an object, and it is Array.", StringComparison.Ordinal);
    }

    [Test]
    public async Task Optional_JsonNull_CountsAsMissing()
    {
        // Arrange
        var value = JsonElement.Parse("""{"Limit":null}""");

        // Act
        var found = RequestJson.Optional(value, "Limit");

        // Assert
        await Assert.That(found.HasValue).IsFalse();
    }

    [Test]
    [Arguments("1.5")]
    [Arguments("3000000000")]
    public async Task ReadInt_NotAnInt32_ThrowsSerialization(string json)
    {
        // Arrange
        var value = JsonElement.Parse(json);

        // Act
        var act = () => RequestJson.ReadInt(value, "Limit");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage($"The member Limit is not an integer of 32 bits: {json}.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadLong_Fraction_ThrowsSerialization()
    {
        // Arrange
        var value = JsonElement.Parse("1.5");

        // Act
        var act = () => RequestJson.ReadLong(value, "ReadCapacityUnits");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The member ReadCapacityUnits is not an integer of 64 bits: 1.5.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadLong_String_ThrowsSerialization()
    {
        // Arrange
        var value = JsonElement.Parse("\"5\"");

        // Act
        var act = () => RequestJson.ReadLong(value, "ReadCapacityUnits");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The member ReadCapacityUnits must be a number, and it is String.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadEnum_NoneOfAnEnumWithoutIt_ThrowsValidationListingTheValues()
    {
        // Arrange
        var value = JsonElement.Parse("\"NONE\"");

        // Act
        var act = () => RequestJson.ReadEnum(value, "BillingMode", WireNames.BillingModes);

        // Assert
        await Assert.That(act).Throws<FakeError>()
            .WithMessage("1 validation error detected: Value 'NONE' at 'billingMode' failed to satisfy constraint: Member must satisfy enum value set: [PROVISIONED, PAY_PER_REQUEST]", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadEnum_UnknownAttributeTypeOverTheWire_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", FakeCalls.Users.Replace("\"AttributeType\":\"S\"", "\"AttributeType\":\"BOOL\"", StringComparison.Ordinal));

        // Assert
        await Assert.That((answer.Code, answer.Message))
            .IsEqualTo(("ValidationException", "1 validation error detected: Value 'BOOL' at 'attributeDefinitions.1.member.attributeType' failed to satisfy constraint: Member must satisfy enum value set: [B, N, S]"));
    }

    [Test]
    public async Task ReadEnum_None_IsTheZeroMember()
    {
        // Arrange
        var value = JsonElement.Parse("\"NONE\"");

        // Act
        var parsed = RequestJson.ReadEnum(value, "ReturnValues", WireNames.ReturnValues, noneIsDefault: true);

        // Assert
        await Assert.That(parsed).IsEqualTo(ReturnValue.None);
    }

    [Test]
    public async Task ReadStringMap_Entries_ReadsThem()
    {
        // Arrange
        var value = JsonElement.Parse("""{"#a":"a","#b":"b"}""");

        // Act
        var map = RequestJson.ReadStringMap(value, "ExpressionAttributeNames");

        // Assert
        await Assert.That(string.Join(",", map.Select(entry => $"{entry.Key}={entry.Value}"))).IsEqualTo("#a=a,#b=b");
    }

    [Test]
    public async Task ReadStringMap_NumberValue_ThrowsSerialization()
    {
        // Arrange
        var value = JsonElement.Parse("""{"#a":1}""");

        // Act
        var act = () => RequestJson.ReadStringMap(value, "ExpressionAttributeNames");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The member ExpressionAttributeNames must be a string, and it is Number.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadStringList_Strings_ReadsThem()
    {
        // Arrange
        var value = JsonElement.Parse("""["a","b"]""");

        // Act
        var list = RequestJson.ReadStringList(value, "NonKeyAttributes");

        // Assert
        await Assert.That(string.Join(",", list)).IsEqualTo("a,b");
    }

    [Test]
    public async Task ReadArray_Object_ThrowsSerialization()
    {
        // Arrange
        var value = JsonElement.Parse("{}");

        // Act
        var act = () => RequestJson.ReadArray(value, "KeySchema");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The member KeySchema must be an array, and it is Object.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadItem_String_ThrowsSerialization()
    {
        // Arrange
        var value = JsonElement.Parse("\"x\"");

        // Act
        var act = () => RequestJson.ReadItem(value, "Item");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The member Item must be an object, and it is String.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"a":{"S":"x","N":"1"}}""")]
    [Arguments("""{"a":{}}""")]
    [Arguments("""{"a":{"S":"x"},"a":{"S":"y"}}""")]
    [Arguments("""{"a":{"S":"\uD800"}}""")]
    public async Task ReadItem_MalformedValue_ThrowsValidation(string json)
    {
        // Arrange
        var value = JsonElement.Parse(json);

        // Act
        var act = () => RequestJson.ReadItem(value, "Item");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessageContaining("Supplied AttributeValue is not valid in Item: ", StringComparison.Ordinal);
    }
}
