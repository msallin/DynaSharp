using System.Text;
using DynaSharp.Testing;

// These tests pass the values that a key refuses on purpose, to see the builders throw at run time.
#pragma warning disable DYNA122

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>
/// Format <c>raw</c>, on generated builders: every path a placeholder takes, with the keys written by hand. A raw value is a string that keeps its delimiters and nothing else changes, so what
/// a string may not hold it may not hold either. The entities are in <c>KeyModels.cs</c>.
/// </summary>
public sealed class GeneratedRawKeyTests
{
    private const string LoneSurrogate = "a\uD800";

    private static readonly DateOnly Day = new(2026, 11, 2);

    // Delegates of their own: the test framework calls a Func from a data source to get the value of the row.
    public delegate string? RawCall();

    public static IEnumerable<RawCall> InvalidValues() =>
    [
        () => RawKeyed.Keys.Pk.Format(string.Empty),
        () => RawKeyed.Keys.Pk.Format(null!),
        () => RawKeyed.Keys.Pk.Format(LoneSurrogate),
        () => RawKeyed.Keys.Pk.Format("\uDC00b"),
        () => RawKeyed.Keys.Pk.Format("a\u0001"),
        () => RawKeyed.Keys.Pk.Format("a\U0010FFFF"),
        () => RawKeyed.Keys.Pk.Of(string.Empty).Value,
        () => RawKeyed.Keys.Pk.Of(LoneSurrogate).Value,
        () => RawKeyed.Keys.Sk.Format("hq", Day, string.Empty),
        () => RawKeyed.Keys.Sk.PrefixPartial("hq", Day, string.Empty),
        () => RawKeyed.Keys.Sk.PrefixPartial("hq", Day, LoneSurrogate),
        () => RawKeyed.Keys.Sk.Format("h#q", Day, "x"),
        () => RawKeyed.Keys.Sk.Prefix("h#q"),
        () => RawTwin.Keys.StrictPk.Format("a#b"),
    ];

    [Test]
    [Arguments("a#b", "APPOINTMENT#a#b")]
    [Arguments("#", "APPOINTMENT##")]
    [Arguments("a#", "APPOINTMENT#a#")]
    [Arguments("#a", "APPOINTMENT##a")]
    [Arguments("APPOINTMENT#a", "APPOINTMENT#APPOINTMENT#a")]
    [Arguments("é#😀", "APPOINTMENT#é#😀")]
    [Arguments("{x}", "APPOINTMENT#{x}")]
    public async Task Format_ValueThatHoldsTheDelimiter_GoesInAsItIs(string value, string key)
    {
        // Arrange, Act
        var text = RawKeyed.Keys.Pk.Format(value);

        // Assert
        await Assert.That(text).IsEqualTo(key);
    }

    [Test]
    public async Task Format_DelimitersOfTheFieldsBefore_GoInToo()
    {
        // Arrange, Act
        var text = RawKeyed.Keys.Sk.Format("hq", Day, "x#y-z");

        // Assert
        await Assert.That(text).IsEqualTo("hq#2026-11-02#x#y-z");
    }

    [Test]
    [Arguments("plain")]
    [Arguments("é 😀")]
    public async Task Format_ValueWithoutADelimiter_GivesTheBytesOfAStrictTemplate(string value)
    {
        // Arrange, Act
        var (raw, strict) = (RawTwin.Keys.Pk.Format(value), RawTwin.Keys.StrictPk.Format(value));

        // Assert
        await Assert.That(raw).IsEqualTo(strict);
    }

    [Test]
    public async Task Format_ValueLongerThanTheStackBuffer_GoesThroughThePool()
    {
        // Arrange
        var value = string.Concat(Enumerable.Repeat("a#", 200));

        // Act
        var parts = RawKeyed.Keys.Pk.Parse(RawKeyed.Keys.Pk.Format(value));

        // Assert
        await Assert.That(parts.Key).IsEqualTo(value);
    }

    [Test]
    [MethodDataSource(nameof(InvalidValues))]
    public async Task Format_ValueThatNoStringMayBe_Throws(RawCall call)
    {
        // Arrange, Act
        var failure = Assert.Throws<KeyFormatException>(() => call());

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task Of_ValueThatHoldsTheDelimiter_IsTheKey()
    {
        // Arrange, Act
        var key = RawKeyed.Keys.Pk.Of("a#b");

        // Assert
        await Assert.That(key.Value).IsEqualTo("APPOINTMENT#a#b");
    }

    [Test]
    [Arguments("APPOINTMENT#a#b", "a#b")]
    [Arguments("APPOINTMENT##", "#")]
    [Arguments("APPOINTMENT#a#", "a#")]
    [Arguments("APPOINTMENT#é#😀", "é#😀")]
    public async Task Parse_KeyWithDelimitersInTheValue_TakesEverythingToTheEnd(string key, string value)
    {
        // Arrange, Act
        var parsed = (RawKeyed.Keys.Pk.Parse(key).Key, RawKeyed.Keys.Pk.Parse(Encoding.UTF8.GetBytes(key)).Key);

        // Assert
        await Assert.That(parsed).IsEqualTo((value, value));
    }

    [Test]
    public async Task Parse_FieldsBeforeARawValue_StopAtTheirDelimiter()
    {
        // Arrange, Act
        var parts = RawKeyed.Keys.Sk.Parse("hq#2026-11-02#x#y");

        // Assert
        await Assert.That(parts).IsEqualTo(new RawKeyed.Keys.SkParts("hq", Day, "x#y"));
    }

    [Test]
    [Arguments("APPOINTMENT#")]
    [Arguments("APPOINTMENT")]
    [Arguments("OTHER#a")]
    [Arguments("appointment#a")]
    [Arguments("APPOINTMENT#a\u0001")]
    [Arguments("APPOINTMENT#a\U0010FFFF")]
    [Arguments("APPOINTMENT#a\uD800")]
    [Arguments(null)]
    public async Task TryParse_TextThatFormatCannotGive_IsNoKey(string? text)
    {
        // Arrange, Act
        var isKey = RawKeyed.Keys.Pk.TryParse(text, out _);

        // Assert
        await Assert.That(isKey).IsFalse();
    }

    [Test]
    public async Task TryParse_Utf8ThatIsNotWellFormed_IsNoKey()
    {
        // Arrange
        byte[] text = [.. "APPOINTMENT#a"u8, 0xFF];

        // Act
        var isKey = RawKeyed.Keys.Pk.TryParse((ReadOnlySpan<byte>)text, out _);

        // Assert
        await Assert.That(isKey).IsFalse();
    }

    [Test]
    public async Task Prefix_FieldsBeforeARawValue_EndsOnTheirDelimiter()
    {
        // Arrange, Act
        var prefixes = (RawKeyed.Keys.Sk.Prefix("hq"), RawKeyed.Keys.Sk.Prefix("hq", Day), RawKeyed.Keys.Sk.UpperBound("hq", Day));

        // Assert
        await Assert.That(prefixes).IsEqualTo(("hq#", "hq#2026-11-02#", "hq#2026-11-02#\U0010FFFF"));
    }

    [Test]
    public async Task PrefixPartial_ValueThatEndsOnADelimiter_MatchesWhatGoesOn()
    {
        // Arrange, Act
        var prefix = RawKeyed.Keys.Sk.PrefixPartial("hq", Day, "x#");

        // Assert
        await Assert.That(prefix).IsEqualTo("hq#2026-11-02#x#");
    }

    [Test]
    public async Task ReadAttributes_RequiredInputsMissing_AreRestoredFromTheRawKeys()
    {
        // Arrange (an item that an update created has its keys and not the inputs in them)
        const string Json = """{"Id":{"S":"APPOINTMENT#a#b"},"sk":{"S":"hq#2026-11-02#x#y"},"Owner":{"S":"o"},"Seat":{"N":"1"},"Note":{"S":"n"}}""";

        // Act
        var item = EntityJson.Read<RawKeyed>(Json);

        // Assert
        await Assert.That((item.Key, item.Site, item.Day, item.Ref)).IsEqualTo(("a#b", "hq", Day, "x#y"));
    }

    [Test]
    public async Task ReadAttributes_KeyOnlyValue_ComesBackWithItsDelimiters()
    {
        // Arrange
        var item = new RawKeyOnly { Tenant = "t", Path = "a#b/c" };

        // Act
        var read = EntityJson.Read<RawKeyOnly>(EntityJson.Write(item));

        // Assert
        await Assert.That(read).IsEqualTo(item);
    }

    [Test]
    public async Task ToRequestJson_UpdateOfAnIndexThatRepeatsARawValue_ReadsItBackFromTheKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<RawKeyed>("Shop").Update(RawKeyed.Keys.Of("a#b", "hq", Day, "x#y")).Set(RawKeyed.Attrs.Owner, "o").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"Id":{"S":"APPOINTMENT#a#b"},"sk":{"S":"hq#2026-11-02#x#y"}},"UpdateExpression":"SET #n0 = :v0, #n1 = :v1, #n2 = :v2, #n3 = if_not_exists(#n3, :v3)","ExpressionAttributeNames":{"#n0":"Owner","#n1":"byownerpk","#n2":"byownersk","#n3":"_type"},"ExpressionAttributeValues":{":v0":{"S":"o"},":v1":{"S":"OWNER#o"},":v2":{"S":"REF#x#y"},":v3":{"S":"RAWKEYED"}}}""");
    }

    [Test]
    public async Task ToRequestJson_UpdateOfARawInputOfAnIndex_FormatsItWithItsDelimiters()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<RawKeyed>("Shop").Update(RawKeyed.Keys.Of("a#b", "hq", Day, "x#y")).Set(RawKeyed.Attrs.Note, "n#1").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""{":v0":{"S":"n#1"},":v1":{"S":"NOTE#n#1"}""");
    }

    [Test]
    public async Task WriteAttributes_StrictTemplateOfTheSameValue_ThrowsWhereTheRawOneTakesIt()
    {
        // Arrange
        var item = new RawTwin { Key = "a#b" };

        // Act
        var failure = Assert.Throws<KeyFormatException>(() => EntityJson.Write(item));

        // Assert
        await Assert.That(failure.Message).Contains("Key");
    }
}
