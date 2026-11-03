using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The rules of the API reference: PutItem and DeleteItem take ReturnValues NONE and ALL_OLD, UpdateItem all five; ItemCollectionMetrics only for a table with a local index.
public sealed class WriteReturnsTests
{
    private static readonly Item Old = WriteCapacityTests.Item("a", "b", ("v", "1"));
    private static readonly Item New = WriteCapacityTests.Item("a", "b", ("v", "2"));

    [Test]
    [Arguments("PutItem", ReturnValue.AllNew)]
    [Arguments("PutItem", ReturnValue.UpdatedOld)]
    [Arguments("DeleteItem", ReturnValue.UpdatedNew)]
    public async Task Check_ValueTheOperationDoesNotTake_AnswersValidation(string operation, ReturnValue returnValues)
    {
        // Arrange
        // Act
        var act = () => WriteReturns.Check(operation, returnValues);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Return values set to invalid value", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("PutItem", ReturnValue.None)]
    [Arguments("DeleteItem", ReturnValue.AllOld)]
    [Arguments("UpdateItem", ReturnValue.UpdatedNew)]
    [Arguments("UpdateItem", ReturnValue.AllNew)]
    public async Task Check_ValueTheOperationTakes_Passes(string operation, ReturnValue returnValues)
    {
        // Arrange
        // Act
        var act = () => WriteReturns.Check(operation, returnValues);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [Arguments(ReturnValue.None, "{}")]
    [Arguments(ReturnValue.AllOld, """{"Attributes":{"pk":{"S":"a"},"sk":{"S":"b"},"v":{"S":"1"}}}""")]
    [Arguments(ReturnValue.AllNew, """{"Attributes":{"pk":{"S":"a"},"sk":{"S":"b"},"v":{"S":"2"}}}""")]
    [Arguments(ReturnValue.UpdatedOld, """{"Attributes":{"v":{"S":"1"}}}""")]
    [Arguments(ReturnValue.UpdatedNew, """{"Attributes":{"v":{"S":"2"}}}""")]
    public async Task Write_ReturnValues_WritesTheAttributesTheyName(ReturnValue returnValues, string expected)
    {
        // Arrange
        var outcome = Outcome(Old, New, updatedOld: new Item { ["v"] = AttributeValue.FromString("1") }, updatedNew: new Item { ["v"] = AttributeValue.FromString("2") });

        // Act
        var json = WriteCapacityTests.Render(writer => WriteReturns.Write(writer, Plain(), returnValues, ReturnItemCollectionMetrics.None, outcome));

        // Assert
        await Assert.That(FakeCalls.SameJson(expected, json)).IsTrue();
    }

    [Test]
    public async Task Write_AllOldOfANewItem_LeavesAttributesOut()
    {
        // Arrange
        var outcome = Outcome(null, New);

        // Act
        var json = WriteCapacityTests.Render(writer => WriteReturns.Write(writer, Plain(), ReturnValue.AllOld, ReturnItemCollectionMetrics.None, outcome));

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", json)).IsTrue();
    }

    [Test]
    public async Task Write_SizeOnATableWithALocalIndex_WritesTheItemCollectionOfThePartition()
    {
        // Arrange
        var outcome = Outcome(Old, null);

        // Act
        var json = WriteCapacityTests.Render(writer => WriteReturns.Write(writer, WriteCapacityTests.Forum(), ReturnValue.None, ReturnItemCollectionMetrics.Size, outcome));

        // Assert
        await Assert.That(FakeCalls.SameJson("""{"ItemCollectionMetrics":{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0,1]}}""", json)).IsTrue();
    }

    [Test]
    public async Task Write_SizeOnATableWithoutALocalIndex_WritesNoMetrics()
    {
        // Arrange
        var outcome = Outcome(Old, New);

        // Act
        var json = WriteCapacityTests.Render(writer => WriteReturns.Write(writer, Plain(), ReturnValue.None, ReturnItemCollectionMetrics.Size, outcome));

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", json)).IsTrue();
    }

    [Test]
    public async Task WriteAll_TwoTables_WritesAListForTheTableWithALocalIndex()
    {
        // Arrange
        var forum = WriteCapacityTests.Forum();
        (FakeTable, WriteOutcome)[] writes = [(forum, Outcome(null, Old)), (Plain(), Outcome(null, New)), (forum, Outcome(null, WriteCapacityTests.Item("c", "d")))];

        // Act
        var json = WriteCapacityTests.Render(writer => WriteReturns.WriteAll(writer, ReturnItemCollectionMetrics.Size, writes));

        // Assert
        await Assert.That(FakeCalls.SameJson(
            """
            {"ItemCollectionMetrics":{"Forum":[{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0,1]},{"ItemCollectionKey":{"pk":{"S":"c"}},"SizeEstimateRangeGB":[0,1]}]}}
            """,
            json)).IsTrue();
    }

    [Test]
    public async Task WriteAll_None_WritesNothing()
    {
        // Arrange
        (FakeTable, WriteOutcome)[] writes = [(WriteCapacityTests.Forum(), Outcome(null, Old))];

        // Act
        var json = WriteCapacityTests.Render(writer => WriteReturns.WriteAll(writer, ReturnItemCollectionMetrics.None, writes));

        // Assert
        await Assert.That(FakeCalls.SameJson("{}", json)).IsTrue();
    }

    private static FakeTable Plain() => new("Thread", new KeySchema("pk", ScalarAttributeType.String, "sk", ScalarAttributeType.String), [], DateTimeOffset.UnixEpoch);

    private static WriteOutcome Outcome(Item? oldImage, Item? newImage, Item? updatedOld = null, Item? updatedNew = null)
    {
        var key = new ItemKey(AttributeValue.FromString((oldImage ?? newImage)!["pk"].AsString()), AttributeValue.FromString((oldImage ?? newImage)!["sk"].AsString()));
        return new WriteOutcome(key, oldImage, newImage, updatedOld, updatedNew);
    }
}
