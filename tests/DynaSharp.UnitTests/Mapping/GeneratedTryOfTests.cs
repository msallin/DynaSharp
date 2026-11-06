using System.Net;
using DynaSharp.Testing;

// These tests pass the values that a key refuses on purpose, to see the builders throw at run time.
#pragma warning disable DYNA122

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>
/// <c>TryOf</c> against what it stands in for. <c>Format</c>, <c>Of</c> and <c>TryOf</c> run the generated <c>Encode</c>, so the table here covers every encoding that can turn a value away, with values
/// on both sides of its rule. The entities are in <c>KeyModels.cs</c>.
/// </summary>
public sealed class GeneratedTryOfTests
{
    private static readonly DateTime Utc = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    // Delegates of their own: the test framework calls a Func from a data source to get the value of the row.
    public delegate bool TryKey();

    public delegate object? KeyCall();

    public static IEnumerable<KeyCase> Cases() =>
    [
        new("string", true, () => Member.Keys.Pk.TryOf("t", "1") is not null, () => Member.Keys.Pk.Format("t", "1")),
        new("string, é and an emoji", true, () => Member.Keys.Pk.TryOf("é", "😀") is not null, () => Member.Keys.Pk.Format("é", "😀")),
        new("delimiter", false, () => Member.Keys.Pk.TryOf("a#b", "1") is not null, () => Member.Keys.Pk.Format("a#b", "1")),
        new("delimiter in the last field", false, () => Member.Keys.Pk.TryOf("t", "1#USER#2") is not null, () => Member.Keys.Pk.Format("t", "1#USER#2")),
        new("empty", false, () => Member.Keys.Pk.TryOf(string.Empty, "1") is not null, () => Member.Keys.Pk.Format(string.Empty, "1")),
        new("null", false, () => Member.Keys.Pk.TryOf(null!, "1") is not null, () => Member.Keys.Pk.Format(null!, "1")),
        new("control character", false, () => Member.Keys.Pk.TryOf("a\u0001", "1") is not null, () => Member.Keys.Pk.Format("a\u0001", "1")),
        new("U+10FFFF", false, () => Member.Keys.Pk.TryOf("a\U0010FFFF", "1") is not null, () => Member.Keys.Pk.Format("a\U0010FFFF", "1")),
        new("lone surrogate", false, () => Member.Keys.Pk.TryOf("a\uD800", "1") is not null, () => Member.Keys.Pk.Format("a\uD800", "1")),
        new("long value", true, () => Member.Keys.Pk.TryOf(new string('a', 1000), "1") is not null, () => Member.Keys.Pk.Format(new string('a', 1000), "1")),
        new("long value with a delimiter", false, () => Member.Keys.Pk.TryOf(new string('a', 1000) + "#", "1") is not null, () => Member.Keys.Pk.Format(new string('a', 1000) + "#", "1")),
        new("raw with a delimiter", true, () => RawKeyed.Keys.Pk.TryOf("a#b") is not null, () => RawKeyed.Keys.Pk.Format("a#b")),
        new("raw, empty", false, () => RawKeyed.Keys.Pk.TryOf(string.Empty) is not null, () => RawKeyed.Keys.Pk.Format(string.Empty)),
        new("raw, lone surrogate", false, () => RawKeyed.Keys.Pk.TryOf("\uDC00") is not null, () => RawKeyed.Keys.Pk.Format("\uDC00")),
        new("enum by name", true, () => KeyFormats.Keys.NamePk.TryOf(Plan.Pro) is not null, () => KeyFormats.Keys.NamePk.Format(Plan.Pro)),
        new("no member of the enum", false, () => KeyFormats.Keys.NamePk.TryOf((Plan)99) is not null, () => KeyFormats.Keys.NamePk.Format((Plan)99)),
        new("negative enum value", false, () => KeyFormats.Keys.ValuePk.TryOf((Plan)(-1)) is not null, () => KeyFormats.Keys.ValuePk.Format((Plan)(-1))),
        new("plain number", true, () => KeyFormats.Keys.IntPk.TryOf(-1) is not null, () => KeyFormats.Keys.IntPk.Format(-1)),
        new("minus next to a hyphen", false, () => VectorKeys.Keys.V12Pk.TryOf(-1) is not null, () => VectorKeys.Keys.V12Pk.Format(-1)),
        new("number next to a hyphen", true, () => VectorKeys.Keys.V12Pk.TryOf(1) is not null, () => VectorKeys.Keys.V12Pk.Format(1)),
        new("digits", true, () => KeyFormats.Keys.DigitsIntPk.TryOf(int.MaxValue) is not null, () => KeyFormats.Keys.DigitsIntPk.Format(int.MaxValue)),
        new("negative digits", false, () => KeyFormats.Keys.DigitsIntPk.TryOf(-1) is not null, () => KeyFormats.Keys.DigitsIntPk.Format(-1)),
        new("digits of a byte", true, () => KeyFormats.Keys.DigitsBytePk.TryOf(byte.MaxValue) is not null, () => KeyFormats.Keys.DigitsBytePk.Format(byte.MaxValue)),
        new("fixed point", true, () => KeyFormats.Keys.FixedDecimalPk.TryOf(12.5m) is not null, () => KeyFormats.Keys.FixedDecimalPk.Format(12.5m)),
        new("fixed point, too many fraction digits", false, () => KeyFormats.Keys.FixedDecimalPk.TryOf(12.345m) is not null, () => KeyFormats.Keys.FixedDecimalPk.Format(12.345m)),
        new("fixed point, out of range", false, () => KeyFormats.Keys.FixedDecimalPk.TryOf(12345m) is not null, () => KeyFormats.Keys.FixedDecimalPk.Format(12345m)),
        new("plain decimal", true, () => KeyFormats.Keys.DecimalPk.TryOf(1.5m) is not null, () => KeyFormats.Keys.DecimalPk.Format(1.5m)),
        new("time in UTC", true, () => KeyFormats.Keys.DateTimePk.TryOf(Utc) is not null, () => KeyFormats.Keys.DateTimePk.Format(Utc)),
        new("time of no kind", false, () => KeyFormats.Keys.DateTimePk.TryOf(DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified)) is not null, () => KeyFormats.Keys.DateTimePk.Format(DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified))),
        new("custom type", true, () => KeyFormats.Keys.CustomPk.TryOf(IPAddress.Loopback) is not null, () => KeyFormats.Keys.CustomPk.Format(IPAddress.Loopback)),
    ];

    public static IEnumerable<KeyCase> PrimaryKeys() =>
    [
        new("both keys fit", true, () => Member.Keys.TryOf("t", "1", "EU", "Bern") is not null, () => Member.Keys.Of("t", "1", "EU", "Bern")),
        new("the partition key does not", false, () => Member.Keys.TryOf("a#b", "1", "EU", "Bern") is not null, () => Member.Keys.Of("a#b", "1", "EU", "Bern")),
        new("the sort key does not", false, () => Member.Keys.TryOf("t", "1", "EU", "a#b") is not null, () => Member.Keys.Of("t", "1", "EU", "a#b")),
        new("empty sort key value", false, () => Member.Keys.TryOf("t", "1", "EU", string.Empty) is not null, () => Member.Keys.Of("t", "1", "EU", string.Empty)),
        new("beside a plain key", true, () => Account.Keys.TryOf("a", 7) is not null, () => Account.Keys.Of("a", 7)),
        new("a value wider than its format", false, () => Account.Keys.TryOf("a", 1000) is not null, () => Account.Keys.Of("a", 1000)),
        new("a value a plain key takes and the template beside it does not", false, () => Account.Keys.TryOf("a#b", 7) is not null, () => Account.Keys.Of("a#b", 7)),
        new("raw", true, () => RawKeyed.Keys.TryOf("a#b", "hq", new DateOnly(2026, 11, 2), "x#y") is not null, () => RawKeyed.Keys.Of("a#b", "hq", new DateOnly(2026, 11, 2), "x#y")),
        new("a strict field beside a raw one", false, () => RawKeyed.Keys.TryOf("a#b", "h#q", new DateOnly(2026, 11, 2), "x#y") is not null, () => RawKeyed.Keys.Of("a#b", "h#q", new DateOnly(2026, 11, 2), "x#y")),
    ];

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task TryOf_ValueOfATable_AgreesWithFormat(KeyCase row)
    {
        // Arrange
        var throws = Throws(row.Throwing);

        // Act
        var isKey = row.Try();

        // Assert (both against the row, so that a row that is on the wrong side shows)
        await Assert.That((isKey, throws)).IsEqualTo((row.IsKey, !row.IsKey));
    }

    [Test]
    [MethodDataSource(nameof(PrimaryKeys))]
    public async Task TryOf_ValuesOfAPrimaryKey_AgreeWithOf(KeyCase row)
    {
        // Arrange
        var throws = Throws(row.Throwing);

        // Act
        var isKey = row.Try();

        // Assert
        await Assert.That((isKey, throws)).IsEqualTo((row.IsKey, !row.IsKey));
    }

    [Test]
    public async Task TryOf_KeyThatFits_IsTheKeyOf()
    {
        // Arrange
        var expected = Member.Keys.Pk.Of("t", "1");

        // Act
        var key = Member.Keys.Pk.TryOf("t", "1");

        // Assert
        await Assert.That(key is { } found ? (found.Value, found.AttributeName) : default).IsEqualTo((expected.Value, expected.AttributeName));
    }

    [Test]
    public async Task TryOf_ValueThatDoesNotFit_IsNull()
    {
        // Arrange, Act
        var key = Member.Keys.Pk.TryOf("a#b", "1");

        // Assert
        await Assert.That(key.HasValue).IsFalse();
    }

    [Test]
    public async Task TryOf_KeyOfAnIndex_IsTheKeyOfItsBuilder()
    {
        // Arrange
        var expected = RawKeyed.Keys.ByOwnerPk.Of("alice");

        // Act
        var key = RawKeyed.Keys.ByOwnerPk.TryOf("alice");

        // Assert
        await Assert.That(key is { } found ? (found.Value, found.AttributeName) : default).IsEqualTo((expected.Value, expected.AttributeName));
    }

    [Test]
    public async Task TryOf_KeyOfAnIndexThatDoesNotFit_IsNull()
    {
        // Arrange, Act
        var key = RawKeyed.Keys.ByOwnerPk.TryOf("a#b");

        // Assert
        await Assert.That(key.HasValue).IsFalse();
    }

    [Test]
    public async Task TryOf_PrimaryKeyThatFits_WritesWhatOfWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var members = client.Table<Member>("Shop");
        var expected = members.Get(Member.Keys.Of("t", "1", "EU", "Bern")).ToRequestJson();

        // Act
        var json = Member.Keys.TryOf("t", "1", "EU", "Bern") is { } key ? members.Get(key).ToRequestJson() : null;

        // Assert
        await Assert.That(json).IsEqualTo(expected);
    }

    [Test]
    public async Task TryOf_PrimaryKeyThatDoesNotFit_IsNull()
    {
        // Arrange, Act
        var key = Member.Keys.TryOf("t", "1", "EU", "a#b");

        // Assert
        await Assert.That(key.HasValue).IsFalse();
    }

    [Test]
    public async Task TryOf_PlaceholderCalledKey_StillTakesItsValue()
    {
        // Arrange, Act
        var result = RawKeyed.Keys.Pk.TryOf(key: "a#b");

        // Assert
        await Assert.That(result?.Value).IsEqualTo("APPOINTMENT#a#b");
    }

    [Test]
    public async Task TryOf_ValueThatDoesNotFit_AllocatesNothing()
    {
        // Arrange (a first pass compiles what the second measures)
        const string Value = "a#b";
        _ = Member.Keys.Pk.TryOf(Value, "1");

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = Member.Keys.Pk.TryOf(Value, "1");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task TryOf_KeyThatFits_AllocatesWhatOfAllocates()
    {
        // Arrange
        var of = Measure(static () => Member.Keys.Pk.Of("t", "1").Value.Length);

        // Act
        var tryOf = Measure(static () => Member.Keys.Pk.TryOf("t", "1") is { } key ? key.Value.Length : 0);

        // Assert
        await Assert.That(tryOf).IsEqualTo(of);
    }

    [Test]
    public async Task OfAndTryOf_PrimaryKey_AllocateNothing()
    {
        // Arrange (what a span scan costs is the claim: the values go through the stack, no string comes of it, and a nullable struct is no box)
        _ = Member.Keys.Of("t", "1", "EU", "Bern");
        _ = Member.Keys.TryOf("t", "a#b", "EU", "Bern");

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = Member.Keys.Of("t", "1", "EU", "Bern");
            _ = Member.Keys.TryOf("t", "1", "EU", "Bern");
            _ = Member.Keys.TryOf("t", "a#b", "EU", "Bern");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    private static bool Throws(KeyCall call)
    {
        try
        {
            _ = call();
            return false;
        }
        catch (KeyFormatException)
        {
            return true;
        }
    }

    private static long Measure(Func<int> call)
    {
        _ = call();
        var smallest = long.MaxValue;
        for (var i = 0; i < 16; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            _ = call();
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return smallest;
    }
}
