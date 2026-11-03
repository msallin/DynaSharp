using DynaSharp.Testing;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Expressions;

/// <summary>
/// The condition <c>contains</c>, by handle and by <see cref="Attr"/>. The handles are those the generator emits for <see cref="Basket"/>, one property per shape of a set, a list and a string, so a shape that
/// <c>AttrExtensions</c> does not cover fails to compile here.
/// </summary>
internal sealed class ContainsTests
{
    private static readonly Guid Sample = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    public static IEnumerable<ContainsCase> Handles()
    {
        // Sets: the operand is the element type of the set, written as the set holds it.
        yield return new("HashSet<string>", Basket.Attrs.Tags.Contains("a"), "Tags", """{"S":"a"}""");
        yield return new("HashSet<string>?", Basket.Attrs.MaybeTags.Contains("a"), "MaybeTags", """{"S":"a"}""");
        yield return new("[OmitEmpty] HashSet<string>", Basket.Attrs.OmittedTags.Contains("a"), "OmittedTags", """{"S":"a"}""");
        yield return new("[OmitEmpty] HashSet<string>?", Basket.Attrs.MaybeOmittedTags.Contains("a"), "MaybeOmittedTags", """{"S":"a"}""");
        yield return new("IReadOnlySet<long>", Basket.Attrs.Numbers.Contains(5), "Numbers", """{"N":"5"}""");
        yield return new("IReadOnlySet<long>?", Basket.Attrs.MaybeNumbers.Contains(-5), "MaybeNumbers", """{"N":"-5"}""");
        yield return new("[OmitEmpty] IReadOnlySet<long>", Basket.Attrs.OmittedNumbers.Contains(long.MaxValue), "OmittedNumbers", """{"N":"9223372036854775807"}""");
        yield return new("[OmitEmpty] IReadOnlySet<long>?", Basket.Attrs.MaybeOmittedNumbers.Contains(0), "MaybeOmittedNumbers", """{"N":"0"}""");
        yield return new("Guid[] as a set", Basket.Attrs.Ids.Contains(Sample), "Ids", """{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}""");
        yield return new("Guid[]? as a set", Basket.Attrs.MaybeIds.Contains(Sample), "MaybeIds", """{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}""");
        yield return new("[OmitEmpty] Guid[] as a set", Basket.Attrs.OmittedIds.Contains(Sample), "OmittedIds", """{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}""");
        yield return new("[OmitEmpty] Guid[]? as a set", Basket.Attrs.MaybeOmittedIds.Contains(Sample), "MaybeOmittedIds", """{"S":"0f8fad5b-d9cb-469f-a165-70867728950e"}""");
        yield return new("HashSet<byte[]>", Basket.Attrs.Blobs.Contains([1, 2]), "Blobs", """{"B":"AQI="}""");
        yield return new("HashSet<decimal>", Basket.Attrs.Prices.Contains(1.5m), "Prices", """{"N":"1.5"}""");
        yield return new("HashSet of an enum", Basket.Attrs.Shades.Contains(Shade.Green), "Shades", """{"S":"Green"}""");

        // Lists: the operand is an element, written by the converter of the elements.
        yield return new("List<string>", Basket.Attrs.Lines.Contains("a"), "Lines", """{"S":"a"}""");
        yield return new("List<string>?", Basket.Attrs.MaybeLines.Contains("a"), "MaybeLines", """{"S":"a"}""");
        yield return new("[OmitEmpty] List<string>", Basket.Attrs.OmittedLines.Contains("a"), "OmittedLines", """{"S":"a"}""");
        yield return new("[OmitEmpty] List<string>?", Basket.Attrs.MaybeOmittedLines.Contains("a"), "MaybeOmittedLines", """{"S":"a"}""");
        yield return new("IReadOnlyList<int>", Basket.Attrs.Counts.Contains(int.MinValue), "Counts", """{"N":"-2147483648"}""");
        yield return new("IReadOnlyList<int>?", Basket.Attrs.MaybeCounts.Contains(1), "MaybeCounts", """{"N":"1"}""");
        yield return new("[OmitEmpty] IReadOnlyList<int>", Basket.Attrs.OmittedCounts.Contains(-1), "OmittedCounts", """{"N":"-1"}""");
        yield return new("[OmitEmpty] IReadOnlyList<int>?", Basket.Attrs.MaybeOmittedCounts.Contains(int.MaxValue), "MaybeOmittedCounts", """{"N":"2147483647"}""");
        yield return new("IList<bool>", Basket.Attrs.Flags.Contains(true), "Flags", """{"BOOL":true}""");
        yield return new("IList<bool>?", Basket.Attrs.MaybeFlags.Contains(false), "MaybeFlags", """{"BOOL":false}""");
        yield return new("[OmitEmpty] IList<bool>", Basket.Attrs.OmittedFlags.Contains(true), "OmittedFlags", """{"BOOL":true}""");
        yield return new("[OmitEmpty] IList<bool>?", Basket.Attrs.MaybeOmittedFlags.Contains(true), "MaybeOmittedFlags", """{"BOOL":true}""");
        yield return new("Shade[]", Basket.Attrs.Palette.Contains(Shade.Red), "Palette", """{"S":"Red"}""");
        yield return new("Shade[]?", Basket.Attrs.MaybePalette.Contains(Shade.Red), "MaybePalette", """{"S":"Red"}""");
        yield return new("[OmitEmpty] Shade[]", Basket.Attrs.OmittedPalette.Contains(Shade.Red), "OmittedPalette", """{"S":"Red"}""");
        yield return new("[OmitEmpty] Shade[]?", Basket.Attrs.MaybeOmittedPalette.Contains(Shade.Red), "MaybeOmittedPalette", """{"S":"Red"}""");
        yield return new("List<DateOnly>", Basket.Attrs.Days.Contains(new DateOnly(2026, 11, 3)), "Days", """{"S":"2026-11-03"}""");

        // Strings: the operand is a substring.
        yield return new("string", Basket.Attrs.Name.Contains("li"), "Name", """{"S":"li"}""");
        yield return new("string?", Basket.Attrs.Nickname.Contains("li"), "Nickname", """{"S":"li"}""");
        yield return new("[OmitEmpty] string", Basket.Attrs.Note.Contains(string.Empty), "Note", """{"S":""}""");
        yield return new("[OmitEmpty] string?", Basket.Attrs.Remark.Contains("ünï"), "Remark", """{"S":"ünï"}""");
    }

    [Test]
    [MethodDataSource(nameof(Handles))]
    public async Task Contains_EveryShapeOfHandle_RendersTheFunctionWithTheOperandAsTheAttributeStoresIt(ContainsCase shape)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(shape.Condition).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            $$$"""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"ConditionExpression":"contains(#n0, :v0)","ExpressionAttributeNames":{"#n0":"{{{shape.Attribute}}}"},"ExpressionAttributeValues":{":v0":{{{shape.Value}}}}}""");
    }

    [Test]
    public async Task Contains_Negated_RendersNotAroundTheFunction()
    {
        // Arrange (the idempotent counter: the write holds only for an id the set does not hold, and for an item that has no set yet)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("b1").Add(Basket.Attrs.Tags, ["request-1"]).If(Basket.Attrs.Tags.Contains("request-1").Negate()).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TableName":"Shop","Key":{"pk":{"S":"b1"}},"UpdateExpression":"ADD #n0 :v0","ConditionExpression":"NOT (contains(#n0, :v1))","ExpressionAttributeNames":{"#n0":"Tags"},"ExpressionAttributeValues":{":v0":{"SS":["request-1"]},":v1":{"S":"request-1"}}}""");
    }

    [Test]
    public async Task Contains_InAFilterWithAnotherCondition_IsOneTermOfTheGroup()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Scan().Filter(Basket.Attrs.Lines.Contains("gift").And(Basket.Attrs.Name.Contains("gift")).Or(Basket.Attrs.Name.Eq("x"))).ToRequestJson();

        // Assert (equal values share a placeholder)
        await Assert.That(json).Contains(
            """"FilterExpression":"((contains(#n0, :v0) AND contains(#n1, :v0)) OR #n1 = :v1)","ExpressionAttributeNames":{"#n0":"Lines","#n1":"Name"},"ExpressionAttributeValues":{":v0":{"S":"gift"},":v1":{"S":"x"}}"""");
    }

    [Test]
    public async Task Contains_OnTheHeap_RendersTheSame()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        Condition kept = Basket.Attrs.Tags.Contains("a");

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(kept & Attr("n").Eq(1)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""ConditionExpression":"(contains(#n0, :v0) AND #n1 = :v1)","ExpressionAttributeNames":{"#n0":"Tags","#n1":"n"}"""");
    }

    [Test]
    public async Task Contains_AttrOfAPathWithReservedWords_PutsEveryNameIntoAPlaceholder()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(Attr("size.lines[2]").Contains("a")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""ConditionExpression":"contains(#n0.#n1[2], :v0)","ExpressionAttributeNames":{"#n0":"size","#n1":"lines"},"ExpressionAttributeValues":{":v0":{"S":"a"}}"""");
    }

    [Test]
    public async Task Contains_AttrWithAnAttributeValue_SendsTheValueAsItIs()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(Attr("blobs").Contains(AttributeValue.FromBinary([1, 2]))).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""""ConditionExpression":"contains(#n0, :v0)","ExpressionAttributeNames":{"#n0":"blobs"},"ExpressionAttributeValues":{":v0":{"B":"AQI="}}"""");
    }

    [Test]
    public async Task Contains_AttrWithAString_SendsAString()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(Attr("tags").Contains("gift")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"S":"gift"}
            """);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(long.MaxValue)]
    public async Task Contains_AttrWithANumber_SendsANumber(long number)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(Attr("numbers").Contains(number)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains($$"""
            ":v0":{"N":"{{number}}"}
            """);
    }

    [Test]
    public async Task Contains_AttrWithADecimal_SendsItsDigits()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk").Update("a").If(Attr("prices").Contains(1.50m)).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"N":"1.50"}
            """);
    }

    /// <summary>A handle, the condition it made, and what goes to DynamoDB. The display name in test output is <paramref name="Name"/>.</summary>
    /// <param name="Name">The shape of the property.</param>
    /// <param name="Condition">The condition.</param>
    /// <param name="Attribute">The name of the attribute.</param>
    /// <param name="Value">The operand as DynamoDB JSON.</param>
    internal readonly record struct ContainsCase(string Name, Condition Condition, string Attribute, string Value)
    {
        public override string ToString() => Name;
    }
}
