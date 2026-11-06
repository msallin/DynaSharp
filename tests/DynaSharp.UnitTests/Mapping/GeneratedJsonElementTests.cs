using System.Text.Json;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>A property of type <see cref="JsonElement"/> through the generated code: stored as the native value of its JSON, and refused with the property and the path in the JSON.</summary>
public sealed class GeneratedJsonElementTests
{
    private const string Large = "The JSON number at $.n has a magnitude of 1E+126 or more, beyond the 9.9999999999999999999999999999999999999E+125 that DynamoDB stores.";

    private static readonly Consignment Plain = new() { Id = "c1", Label = JsonElement.Parse("""{"to":"Bern","weight":1.50,"missing":null}""") };

    public static IEnumerable<Func<(Consignment Consignment, string Message)>> Refusals() =>
    [
        () => (Plain with { Label = JsonElement.Parse("""{"n":1E+126}""") }, "The property Label of Consignment, the attribute \"Label\", cannot be written: " + Large),
        () => (Plain with { Label = default }, "The property Label of Consignment, the attribute \"Label\", cannot be written: A default JsonElement holds no JSON. Leave the attribute out with a JsonElement? that is null."),
        () => (Plain with { Note = JsonElement.Parse("""{"n":1E+126}""") }, "The property Note of Consignment, the attribute \"Note\", cannot be written: " + Large),
        () => (Plain with { Parts = [JsonElement.Parse("1"), JsonElement.Parse("""{"n":1E+126}""")] }, "The property Parts of Consignment, the attribute \"Parts\", cannot be written: " + Large),
        () => (Plain with { Stop = new ConsignmentStop(JsonElement.Parse("""{"n":1E+126}""")) }, "The property Address of ConsignmentStop, the attribute \"Address\", cannot be written: " + Large),
    ];

    [Test]
    public async Task ItemFrom_JsonElementProperties_StoresTheirJsonNatively()
    {
        // Arrange
        var consignment = Plain with { Parts = [JsonElement.Parse("[1]"), JsonElement.Parse("null")], Stop = new ConsignmentStop(JsonElement.Parse("\"Bern\"")) };

        // Act
        var item = Item.From(consignment);

        // Assert
        await Assert.That(item.ToString()).IsEqualTo(
            """{"_type":{"S":"CONSIGNMENT"},"Id":{"S":"c1"},"Label":{"M":{"to":{"S":"Bern"},"weight":{"N":"1.50"}}},"Parts":{"L":[{"L":[{"N":"1"}]},{"NULL":true}]},"Stop":{"M":{"Address":{"S":"Bern"}}}}""");
    }

    [Test]
    public async Task To_StoredItem_ReadsTheJsonBack()
    {
        // Arrange
        var item = Item.From(Plain with { Note = JsonElement.Parse("[true]"), Parts = [JsonElement.Parse("{}")], Stop = new ConsignmentStop(JsonElement.Parse("2")) });

        // Act
        var back = item.To<Consignment>();

        // Assert
        await Assert.That((back.Label.GetRawText(), back.Note?.GetRawText(), back.Parts![0].GetRawText(), back.Stop!.Address.GetRawText()))
            .IsEqualTo(("""{"to":"Bern","weight":1.50}""", "[true]", "{}", "2"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task To_NoteMissingOrNull_ReadsAsNull(bool storedAsNull)
    {
        // Arrange
        var item = Item.From(Plain);
        if (storedAsNull)
        {
            item["Note"] = AttributeValue.Null;
        }

        // Act
        var back = item.To<Consignment>();

        // Assert
        await Assert.That(back.Note).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(Refusals))]
    public async Task ItemFrom_JsonDynamoDbCannotStore_ThrowsArgumentExceptionNamingThePropertyAndThePath(Consignment consignment, string message)
    {
        // Arrange
        // Act
        var act = () => Item.From(consignment);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentException>().WithMessage(message, StringComparison.Ordinal);
    }
}
