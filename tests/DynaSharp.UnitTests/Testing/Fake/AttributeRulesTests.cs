using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// Developer Guide, "Naming rules and data types": sets hold no duplicates, documents nest up to 32 levels, numbers have 38 digits.
public sealed class AttributeRulesTests
{
    public static IEnumerable<Func<(AttributeValue Set, string Listed)>> Duplicates()
    {
        yield return () => (AttributeValue.FromNumberSet(["1", "1.0"]), "1, 1.0");
        yield return () => (AttributeValue.WrapStringSet(["a", "a"]), "a, a");
        yield return () => (AttributeValue.WrapBinarySet([new byte[] { 1 }, new byte[] { 1 }]), "AQ==, AQ==");
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
    [MethodDataSource(nameof(Duplicates))]
    public async Task Normalize_SetWithADuplicate_ThrowsValidation(AttributeValue set, string listed)
    {
        // Arrange
        var item = new Item { ["set"] = set };

        // Act
        var act = () => AttributeRules.Normalize(item);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage($"One or more parameter values were invalid: Input collection [{listed}] contains duplicates.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(32)]
    [Arguments(33)]
    public async Task Normalize_NestedMaps_AllowsThirtyTwoLevels(int levels)
    {
        // Arrange
        var value = AttributeValue.FromString("leaf");
        for (var i = 0; i < levels; i++)
        {
            value = i % 2 == 0 ? AttributeValue.FromMap([KeyValuePair.Create("m", value)]) : AttributeValue.FromList([value]);
        }

        // Act
        var act = () => AttributeRules.Normalize(value);

        // Assert
        if (levels <= AttributeRules.MaxNesting)
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
