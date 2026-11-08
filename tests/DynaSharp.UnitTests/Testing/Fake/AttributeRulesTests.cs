using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// Developer Guide, "Naming rules and data types": sets hold no duplicates, documents nest up to 32 levels, numbers have 38 digits.
public sealed class AttributeRulesTests
{
    // The members DynamoDB names: those before the first repeat in byte order, the repeat, and the rest as sent.
    public static IEnumerable<Func<(AttributeValue Set, string Message)>> Duplicates()
    {
        yield return () => (AttributeValue.FromNumberSet(["1", "1.0"]), "Input collection contains duplicates.");
        yield return () => (AttributeValue.WrapStringSet(["a", "a"]), "Input collection [\"a\", \"a\"] contains duplicates.");
        yield return () => (AttributeValue.WrapStringSet(["b", "a", "b", "c"]), "Input collection [\"a\", \"b\", \"b\", \"c\"] contains duplicates.");
        yield return () => (AttributeValue.WrapBinarySet([new byte[] { 255 }, new byte[] { 0 }, new byte[] { 0 }]), "Input collection [\"AA==\",\"/w==\",\"AA==\"]of type BS contains duplicates.");
        yield return () => (AttributeValue.WrapBinarySet([new byte[] { 2 }, new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 }]), "Input collection [\"AQ==\",\"Ag==\",\"Ag==\",\"Aw==\"]of type BS contains duplicates.");
    }

    public static IEnumerable<Func<(AttributeValue Set, string Expected)>> Unordered()
    {
        yield return () => (AttributeValue.WrapStringSet(["b", string.Empty, "a"]), """{"SS":["","a","b"]}""");
        yield return () => (AttributeValue.WrapStringSet(["é", "z"]), """{"SS":["z","é"]}""");
        yield return () => (AttributeValue.WrapStringSet(["\U0001F600", "\uFF5E"]), AttributeValue.WrapStringSet(["\uFF5E", "\U0001F600"]).ToString());
        yield return () => (AttributeValue.FromNumberSet(["10", "9", "-1"]), """{"NS":["-1","9","10"]}""");
        yield return () => (AttributeValue.WrapBinarySet([new byte[] { 2 }, new byte[] { 1, 0 }, Array.Empty<byte>()]), """{"BS":["","AQA=","Ag=="]}""");
    }

    [Test]
    public async Task Normalize_NumbersAtAnyDepth_GivesTheirCanonicalText()
    {
        // Arrange
        var item = new Item
        {
            ["n"] = AttributeValue.FromNumber("01.50"),
            ["l"] = AttributeValue.FromList([AttributeValue.FromNumber("2.0")]),
            ["m"] = AttributeValue.FromMap([KeyValuePair.Create("k", AttributeValue.FromNumber("-0"))]),
            ["ns"] = AttributeValue.FromNumberSet(["1.0", "20"]),
            ["s"] = "keep",
            ["bs"] = AttributeValue.FromBinarySet([[1]]),
        };

        // Act
        var normalized = AttributeRules.Normalize(item);

        // Assert
        await Assert.That(normalized.ToString()).IsEqualTo("""{"n":{"N":"1.5"},"l":{"L":[{"N":"2"}]},"m":{"M":{"k":{"N":"0"}}},"ns":{"NS":["1","20"]},"s":{"S":"keep"},"bs":{"BS":["AQ=="]}}""");
    }

    [Test]
    [MethodDataSource(nameof(Unordered))]
    public async Task Normalize_Sets_ComeInDynamoDbOrder(AttributeValue set, string expected)
    {
        // Arrange: strings and binaries order by unsigned bytes (strings by UTF-8, not UTF-16), numbers by value.
        var item = new Item { ["set"] = set };

        // Act
        var normalized = AttributeRules.Normalize(item);

        // Assert
        await Assert.That(normalized["set"].ToString()).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Duplicates))]
    public async Task Normalize_SetWithADuplicate_ThrowsValidation(AttributeValue set, string message)
    {
        // Arrange
        var item = new Item { ["set"] = set };

        // Act
        var act = () => AttributeRules.Normalize(item);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("One or more parameter values were invalid: " + message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments(31)]
    [Arguments(32)]
    public async Task Normalize_NestedMaps_AllowsThirtyTwoLevels(int levels)
    {
        // Arrange: levels documents around a string; DynamoDB counts the string as a level too, so 31 documents are the 32 levels it allows.
        var value = AttributeValue.FromString("leaf");
        for (var i = 0; i < levels; i++)
        {
            value = i % 2 == 0 ? AttributeValue.FromMap([KeyValuePair.Create("m", value)]) : AttributeValue.FromList([value]);
        }

        // Act
        var act = () => AttributeRules.Normalize(value);

        // Assert
        if (levels < AttributeRules.MaxNesting)
        {
            await Assert.That(act).ThrowsNothing();
        }
        else
        {
            await Assert.That(act).Throws<FakeError>().WithMessage("Nesting Levels have exceeded supported limits", StringComparison.Ordinal);
        }
    }

    [Test]
    [Arguments(AttributeType.String, "S")]
    [Arguments(AttributeType.Number, "N")]
    [Arguments(AttributeType.Binary, "B")]
    [Arguments(AttributeType.Boolean, "BOOL")]
    [Arguments(AttributeType.Null, "NULL")]
    [Arguments(AttributeType.List, "L")]
    [Arguments(AttributeType.Map, "M")]
    [Arguments(AttributeType.StringSet, "SS")]
    [Arguments(AttributeType.NumberSet, "NS")]
    [Arguments(AttributeType.BinarySet, "BS")]
    [Arguments(AttributeType.None, "NONE")]
    public async Task Descriptor_Type_IsTheDescriptorOfDynamoDbJson(AttributeType type, string expected)
    {
        // Arrange
        // Act
        var descriptor = AttributeRules.Descriptor(type);

        // Assert
        await Assert.That(descriptor).IsEqualTo(expected);
    }
}
