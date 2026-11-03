using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The key rules of the Developer Guide: a partition key of up to 2048 bytes, a sort key of up to 1024, no empty string or binary in a key, and the messages DynamoDB sends.
internal sealed class KeySchemaTests
{
    private static readonly KeySchema Composite = new("pk", ScalarAttributeType.String, "sk", ScalarAttributeType.Binary);
    private static readonly KeySchema Simple = new("id", ScalarAttributeType.Number);

    public static IEnumerable<Func<(Item Key, string Message)>> BadKeys()
    {
        const string NoMatch = "The provided key element does not match the schema";
        yield return () => (new Item { ["pk"] = "a" }, NoMatch);
        yield return () => (new Item { ["pk"] = "a", ["other"] = new byte[] { 1 } }, NoMatch);
        yield return () => (new Item { ["x"] = "a", ["sk"] = new byte[] { 1 } }, NoMatch);
        yield return () => (new Item { ["pk"] = "a", ["sk"] = "b" }, NoMatch);
        yield return () => (new Item { ["pk"] = "a", ["sk"] = Array.Empty<byte>() }, "One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty binary value. Key: sk");
        yield return () => (new Item { ["pk"] = new string('a', 2049), ["sk"] = new byte[] { 1 } }, "One or more parameter values were invalid: Size of hashkey has exceeded the maximum size limit of 2048 bytes");
        yield return () => (new Item { ["pk"] = "a", ["sk"] = new byte[1025] }, "One or more parameter values were invalid: Aggregated size of all range keys has exceeded the size limit of 1024 bytes");
    }

    public static IEnumerable<Func<(Item Item, string Message)>> BadItems()
    {
        yield return () => (new Item { ["sk"] = new byte[] { 1 } }, "One or more parameter values were invalid: Missing the key pk in the item");
        yield return () => (new Item { ["pk"] = true, ["sk"] = new byte[] { 1 } }, "One or more parameter values were invalid: Type mismatch for key pk expected: S actual: BOOL");
        yield return () => (new Item { ["pk"] = string.Empty, ["sk"] = new byte[] { 1 } }, "One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty string value. Key: pk");
    }

    public static IEnumerable<Func<(KeySchema Schema, Item Item, string Message)>> BadIndexKeys()
    {
        yield return () => (Composite, new Item { ["pk"] = 1 },
            "One or more parameter values were invalid: Type mismatch for Index Key pk Expected: S Actual: N IndexName: ByX");
        yield return () => (Composite, new Item { ["sk"] = Array.Empty<byte>() },
            "One or more parameter values are not valid. A value specified for a secondary index key is not supported. The AttributeValue for a key attribute cannot contain an empty binary value. IndexName: ByX, IndexKey: sk");
        yield return () => (Simple, new Item { ["id"] = "1" },
            "One or more parameter values were invalid: Type mismatch for Index Key id Expected: N Actual: S IndexName: ByX");
        yield return () => (new KeySchema("g", ScalarAttributeType.String), new Item { ["g"] = string.Empty },
            "One or more parameter values are not valid. A value specified for a secondary index key is not supported. The AttributeValue for a key attribute cannot contain an empty string value. IndexName: ByX, IndexKey: g");
    }

    public static IEnumerable<Func<(Action Act, bool Expected)>> Failures()
    {
        yield return () => (() => Composite.KeyOf(new Item { ["pk"] = string.Empty, ["sk"] = new byte[] { 1 } }), true);
        yield return () => (() => Composite.ReadKey(new Item { ["pk"] = "a", ["sk"] = Array.Empty<byte>() }), true);
        yield return () => (() => new KeySchema("g", ScalarAttributeType.String).ThrowIfInvalidIndexKey(new Item { ["g"] = string.Empty }, "ByX"), true);
        yield return () => (() => Composite.KeyOf(new Item { ["pk"] = true, ["sk"] = new byte[] { 1 } }), false);
        yield return () => (() => Composite.ReadKey(new Item { ["pk"] = new string('a', 2049), ["sk"] = new byte[] { 1 } }), false);
        yield return () => (() => Composite.ThrowIfInvalidIndexKey(new Item { ["pk"] = 1 }, "ByX"), false);
    }

    public static IEnumerable<Func<(KeySchema Schema, Item Item, bool Expected)>> Coverage()
    {
        yield return () => (Composite, new Item { ["pk"] = "a", ["sk"] = new byte[] { 1 } }, true);
        yield return () => (Composite, new Item { ["pk"] = "a" }, false);
        yield return () => (Composite, new Item { ["pk"] = 1, ["sk"] = new byte[] { 1 } }, false);
        yield return () => (Composite, new Item { ["pk"] = "a", ["sk"] = "b" }, false);
        yield return () => (Simple, new Item { ["id"] = 1 }, true);
        yield return () => (Simple, [], false);
        yield return () => (Simple, new Item { ["id"] = new byte[] { 1 } }, false);
    }

    [Test]
    public async Task ReadKey_KeyOfTheSchema_NormalizesTheNumber()
    {
        // Arrange
        var key = new Item { ["id"] = AttributeValue.FromNumber("7.0") };

        // Act
        var read = Simple.ReadKey(key);

        // Assert
        await Assert.That(read).IsEqualTo(new ItemKey(AttributeValue.FromNumber("7"), null));
    }

    [Test]
    [MethodDataSource(nameof(BadKeys))]
    public async Task ReadKey_KeyThatDoesNotFit_ThrowsValidation(Item key, string message)
    {
        // Arrange
        // Act
        var act = () => Composite.ReadKey(key);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadKey_LargestKeys_AreAccepted()
    {
        // Arrange
        var key = new Item { ["pk"] = new string('a', 2048), ["sk"] = new byte[1024] };

        // Act
        var act = () => Composite.ReadKey(key);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [MethodDataSource(nameof(BadItems))]
    public async Task KeyOf_ItemThatDoesNotFit_ThrowsValidation(Item item, string message)
    {
        // Arrange
        // Act
        var act = () => Composite.KeyOf(item);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task KeyOf_ItemWithMore_GivesItsKey()
    {
        // Arrange
        var item = new Item { ["pk"] = "a", ["sk"] = new byte[] { 1 }, ["x"] = 1 };

        // Act
        var key = Composite.KeyOf(item);

        // Assert
        await Assert.That(Composite.ToItem(key).ToString()).IsEqualTo("""{"pk":{"S":"a"},"sk":{"B":"AQ=="}}""");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ThrowIfInvalidIndexKey_ItemWithoutOrWithFittingKeys_Passes(bool withKeys)
    {
        // Arrange
        var item = withKeys ? new Item { ["pk"] = "a", ["sk"] = new byte[] { 1 } } : new Item { ["x"] = 1 };

        // Act
        var act = () => Composite.ThrowIfInvalidIndexKey(item, "ByX");

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [MethodDataSource(nameof(BadIndexKeys))]
    public async Task ThrowIfInvalidIndexKey_KeyThatDoesNotFit_ThrowsValidation(KeySchema schema, Item item, string message)
    {
        // Arrange
        // Act
        var act = () => schema.ThrowIfInvalidIndexKey(item, "ByX");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(Failures))]
    public async Task Failure_EmptyValueOrAnother_IsAnInputErrorOnlyForTheEmptyValue(Action act, bool expected)
    {
        // Arrange (the call that fails comes from Failures)
        // Act
        var error = Assert.Throws<FakeError>(act);

        // Assert
        await Assert.That(error.IsInputError).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Coverage))]
    public async Task Covers_Item_IsTrueOnlyWithTheWholeKeyOfTheDeclaredTypes(KeySchema schema, Item item, bool expected)
    {
        // Arrange
        // Act
        var covers = schema.Covers(item);

        // Assert
        await Assert.That(covers).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ScalarAttributeType.String, "S")]
    [Arguments(ScalarAttributeType.Number, "N")]
    [Arguments(ScalarAttributeType.Binary, "B")]
    [Arguments(ScalarAttributeType.Unknown, "Unknown")]
    public async Task Descriptor_Type_IsItsWireName(ScalarAttributeType type, string expected)
    {
        // Arrange
        // Act
        var descriptor = KeySchema.Descriptor(type);

        // Assert
        await Assert.That(descriptor).IsEqualTo(expected);
    }

    [Test]
    public async Task Names_Composite_ArePartitionThenSort()
    {
        // Arrange
        // Act
        var names = Composite.Names;

        // Assert
        await Assert.That(string.Join(",", names)).IsEqualTo("pk,sk");
    }
}
