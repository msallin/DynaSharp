using System.Text.Json;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The 32 levels are those of the Developer Guide, "Document types": a value counts one level and each map or list around it one more. The wording by place is what the Parity Suite
// recorded of DynamoDB (tier3/limits/nestingDepth and tier3/validation-ordering/deepRequests).
public sealed class ItemShapeTests
{
    private const string Nesting = "Nesting Levels have exceeded supported limits: Attributes in the item have nested levels beyond supported limit";
    private const string Invalid = "One or more parameter values were invalid: ";

    [Test]
    [Arguments("""["a"]""")]
    [Arguments("\"a\"")]
    [Arguments("""{"a":"x"}""")]
    [Arguments("""{"a":{"S":"x","N":"1"}}""")]
    [Arguments("""{"a":{"M":[]}}""")]
    [Arguments("""{"a":{"L":{}}}""")]
    [Arguments("""{"a":{"SS":"x"}}""")]
    [Arguments("""{"a":{"NULL":true}}""")]
    [Arguments("""{"a":{"N":"1"},"b":{"L":[{"S":"x"},{"BOOL":false}]}}""")]
    public async Task Check_ValueOfAnotherShape_LeavesItToTheReader(string json)
    {
        // Arrange
        var map = Parse(json);

        // Act
        var act = () => ItemShape.Check(map, "Item", single: true);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [Arguments("""{"":{"S":"x"}}""")]
    [Arguments("""{"a":{"M":{"":{"S":"x"}}}}""")]
    [Arguments("""{"a":{"M":{"b":{"M":{"":{"S":"x"}}}}}}""")]
    public async Task Check_AttributeWithoutAName_ThrowsValidation(string json)
    {
        // Arrange
        var map = Parse(json);

        // Act
        var act = () => ItemShape.Check(map, "Item", single: true);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Empty attribute name", StringComparison.Ordinal);
    }

    [Test]
    public async Task Check_TopLevelNamelessAttributeBesideADeepValue_ReportsTheNameFirst()
    {
        // Arrange
        var map = Parse($$$"""{"d":{{{Deep(40)}}},"":{"S":"x"}}""");

        // Act
        var act = () => ItemShape.Check(map, "Item", single: true);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Empty attribute name", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("SS", "An string set  may not be empty")]
    [Arguments("NS", "An number set  may not be empty")]
    [Arguments("BS", "Binary sets should not be empty")]
    public async Task Check_EmptySet_ThrowsValidation(string type, string message)
    {
        // Arrange
        var map = Parse($$$"""{"a":{"L":[{"{{{type}}}":[]}]}}""");

        // Act
        var act = () => ItemShape.Check(map, "Item", single: true);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(Invalid + message, StringComparison.Ordinal);
    }

    [Test]
    public async Task Check_NullThatIsFalse_ThrowsValidation()
    {
        // Arrange
        var map = Parse("""{"a":{"NULL":false}}""");

        // Act
        var act = () => ItemShape.Check(map, "Item", single: true);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(Invalid + "Null attribute value types must have the value of true", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(31)]
    [Arguments(1)]
    public async Task Check_AtTheLimit_Passes(int wraps)
    {
        // Arrange: 31 maps around a string are 32 levels, the deepest DynamoDB stores
        var map = Parse($$$"""{"d":{{{Deep(wraps)}}},"l":{{{DeepList(wraps)}}}}""");

        // Act
        var act = () => ItemShape.Check(map, "Item", single: true);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [Arguments("Item", true, 32, "1 validation error detected: " + Nesting)]
    [Arguments("Item", true, 62, "1 validation error detected: " + Nesting)]
    [Arguments("Item", true, 63, Nesting)]
    [Arguments("Item", false, 32, Nesting)]
    [Arguments("Item", false, 100, Nesting)]
    [Arguments("Key", true, 32, Nesting)]
    [Arguments("Key", false, 32, Nesting)]
    [Arguments("ExpressionAttributeValues", true, 32, "1 validation error detected: " + Nesting)]
    [Arguments("ExpressionAttributeValues", true, 62, "1 validation error detected: " + Nesting)]
    [Arguments("ExpressionAttributeValues", true, 63, "ExpressionAttributeValues contains invalid value: " + Nesting + " for key :deep")]
    [Arguments("ExpressionAttributeValues", false, 32, Nesting)]
    [Arguments("ExpressionAttributeValues", false, 59, Nesting)]
    [Arguments("ExpressionAttributeValues", false, 60, "ExpressionAttributeValues contains invalid value: " + Nesting + " for key :deep")]
    public async Task Check_PastTheLimit_WordsItByPlaceAndDepth(string member, bool alone, int wraps, string message)
    {
        // Arrange: wraps maps around a string are wraps + 1 levels
        var name = member == "ExpressionAttributeValues" ? ":deep" : "deep";
        var map = Parse($$$"""{"{{{name}}}":{{{Deep(wraps)}}}}""");

        // Act
        var act = () => ItemShape.Check(map, member, alone);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task Check_ListPastTheLimit_ThrowsValidation()
    {
        // Arrange
        var map = Parse($$$"""{"d":{{{DeepList(32)}}}}""");

        // Act
        var act = () => ItemShape.Check(map, "Item", single: false);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(Nesting, StringComparison.Ordinal);
    }

    [Test]
    public async Task Check_ValueFarBeyondTheReaderOfTheClient_IsAnsweredWithTheNestingError()
    {
        // Arrange: 600 levels of JSON are far past the 128 the reader of the client takes
        var map = Parse($$$"""{"d":{{{Deep(300)}}}}""");

        // Act
        var act = () => ItemShape.Check(map, "Item", single: false);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(Nesting, StringComparison.Ordinal);
    }

    // The fake reads far deeper requests than the 64 levels of the default, as FakeWire does.
    private static JsonElement Parse(string json) => JsonElement.Parse(json, new JsonDocumentOptions { MaxDepth = 1024 });

    private static string Deep(int wraps) => string.Concat(Enumerable.Repeat("""{"M":{"n":""", wraps)) + """{"S":"leaf"}""" + new string('}', wraps * 2);

    private static string DeepList(int wraps) => string.Concat(Enumerable.Repeat("""{"L":[""", wraps)) + """{"S":"leaf"}""" + string.Concat(Enumerable.Repeat("]}", wraps));
}
