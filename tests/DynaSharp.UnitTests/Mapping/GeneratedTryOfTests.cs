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
        new("string", true, () => Member.Keys.Pk.TryOf("t", "1", out _), () => Member.Keys.Pk.Format("t", "1"), () => Member.Keys.Pk.TryOf("t", "1") is not null),
        new("string, é and an emoji", true, () => Member.Keys.Pk.TryOf("é", "😀", out _), () => Member.Keys.Pk.Format("é", "😀"), () => Member.Keys.Pk.TryOf("é", "😀") is not null),
        new("delimiter", false, () => Member.Keys.Pk.TryOf("a#b", "1", out _), () => Member.Keys.Pk.Format("a#b", "1"), () => Member.Keys.Pk.TryOf("a#b", "1") is not null),
        new("delimiter in the last field", false, () => Member.Keys.Pk.TryOf("t", "1#USER#2", out _), () => Member.Keys.Pk.Format("t", "1#USER#2"), () => Member.Keys.Pk.TryOf("t", "1#USER#2") is not null),
        new("empty", false, () => Member.Keys.Pk.TryOf(string.Empty, "1", out _), () => Member.Keys.Pk.Format(string.Empty, "1"), () => Member.Keys.Pk.TryOf(string.Empty, "1") is not null),
        new("null", false, () => Member.Keys.Pk.TryOf(null!, "1", out _), () => Member.Keys.Pk.Format(null!, "1"), () => Member.Keys.Pk.TryOf(null!, "1") is not null),
        new("control character", false, () => Member.Keys.Pk.TryOf("a\u0001", "1", out _), () => Member.Keys.Pk.Format("a\u0001", "1"), () => Member.Keys.Pk.TryOf("a\u0001", "1") is not null),
        new("U+10FFFF", false, () => Member.Keys.Pk.TryOf("a\U0010FFFF", "1", out _), () => Member.Keys.Pk.Format("a\U0010FFFF", "1"), () => Member.Keys.Pk.TryOf("a\U0010FFFF", "1") is not null),
        new("lone surrogate", false, () => Member.Keys.Pk.TryOf("a\uD800", "1", out _), () => Member.Keys.Pk.Format("a\uD800", "1"), () => Member.Keys.Pk.TryOf("a\uD800", "1") is not null),
        new("long value", true, () => Member.Keys.Pk.TryOf(new string('a', 1000), "1", out _), () => Member.Keys.Pk.Format(new string('a', 1000), "1"), () => Member.Keys.Pk.TryOf(new string('a', 1000), "1") is not null),
        new("long value with a delimiter", false, () => Member.Keys.Pk.TryOf(new string('a', 1000) + "#", "1", out _), () => Member.Keys.Pk.Format(new string('a', 1000) + "#", "1"), () => Member.Keys.Pk.TryOf(new string('a', 1000) + "#", "1") is not null),
        new("raw with a delimiter", true, () => RawKeyed.Keys.Pk.TryOf("a#b", out _), () => RawKeyed.Keys.Pk.Format("a#b"), () => RawKeyed.Keys.Pk.TryOf("a#b") is not null),
        new("raw, empty", false, () => RawKeyed.Keys.Pk.TryOf(string.Empty, out _), () => RawKeyed.Keys.Pk.Format(string.Empty), () => RawKeyed.Keys.Pk.TryOf(string.Empty) is not null),
        new("raw, lone surrogate", false, () => RawKeyed.Keys.Pk.TryOf("\uDC00", out _), () => RawKeyed.Keys.Pk.Format("\uDC00"), () => RawKeyed.Keys.Pk.TryOf("\uDC00") is not null),
        new("enum by name", true, () => KeyFormats.Keys.NamePk.TryOf(Plan.Pro, out _), () => KeyFormats.Keys.NamePk.Format(Plan.Pro), () => KeyFormats.Keys.NamePk.TryOf(Plan.Pro) is not null),
        new("no member of the enum", false, () => KeyFormats.Keys.NamePk.TryOf((Plan)99, out _), () => KeyFormats.Keys.NamePk.Format((Plan)99), () => KeyFormats.Keys.NamePk.TryOf((Plan)99) is not null),
        new("negative enum value", false, () => KeyFormats.Keys.ValuePk.TryOf((Plan)(-1), out _), () => KeyFormats.Keys.ValuePk.Format((Plan)(-1)), () => KeyFormats.Keys.ValuePk.TryOf((Plan)(-1)) is not null),
        new("plain number", true, () => KeyFormats.Keys.IntPk.TryOf(-1, out _), () => KeyFormats.Keys.IntPk.Format(-1), () => KeyFormats.Keys.IntPk.TryOf(-1) is not null),
        new("minus next to a hyphen", false, () => VectorKeys.Keys.V12Pk.TryOf(-1, out _), () => VectorKeys.Keys.V12Pk.Format(-1), () => VectorKeys.Keys.V12Pk.TryOf(-1) is not null),
        new("number next to a hyphen", true, () => VectorKeys.Keys.V12Pk.TryOf(1, out _), () => VectorKeys.Keys.V12Pk.Format(1), () => VectorKeys.Keys.V12Pk.TryOf(1) is not null),
        new("digits", true, () => KeyFormats.Keys.DigitsIntPk.TryOf(int.MaxValue, out _), () => KeyFormats.Keys.DigitsIntPk.Format(int.MaxValue), () => KeyFormats.Keys.DigitsIntPk.TryOf(int.MaxValue) is not null),
        new("negative digits", false, () => KeyFormats.Keys.DigitsIntPk.TryOf(-1, out _), () => KeyFormats.Keys.DigitsIntPk.Format(-1), () => KeyFormats.Keys.DigitsIntPk.TryOf(-1) is not null),
        new("digits of a byte", true, () => KeyFormats.Keys.DigitsBytePk.TryOf(byte.MaxValue, out _), () => KeyFormats.Keys.DigitsBytePk.Format(byte.MaxValue), () => KeyFormats.Keys.DigitsBytePk.TryOf(byte.MaxValue) is not null),
        new("fixed point", true, () => KeyFormats.Keys.FixedDecimalPk.TryOf(12.5m, out _), () => KeyFormats.Keys.FixedDecimalPk.Format(12.5m), () => KeyFormats.Keys.FixedDecimalPk.TryOf(12.5m) is not null),
        new("fixed point, too many fraction digits", false, () => KeyFormats.Keys.FixedDecimalPk.TryOf(12.345m, out _), () => KeyFormats.Keys.FixedDecimalPk.Format(12.345m), () => KeyFormats.Keys.FixedDecimalPk.TryOf(12.345m) is not null),
        new("fixed point, out of range", false, () => KeyFormats.Keys.FixedDecimalPk.TryOf(12345m, out _), () => KeyFormats.Keys.FixedDecimalPk.Format(12345m), () => KeyFormats.Keys.FixedDecimalPk.TryOf(12345m) is not null),
        new("plain decimal", true, () => KeyFormats.Keys.DecimalPk.TryOf(1.5m, out _), () => KeyFormats.Keys.DecimalPk.Format(1.5m), () => KeyFormats.Keys.DecimalPk.TryOf(1.5m) is not null),
        new("time in UTC", true, () => KeyFormats.Keys.DateTimePk.TryOf(Utc, out _), () => KeyFormats.Keys.DateTimePk.Format(Utc), () => KeyFormats.Keys.DateTimePk.TryOf(Utc) is not null),
        new("time of no kind", false, () => KeyFormats.Keys.DateTimePk.TryOf(DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified), out _), () => KeyFormats.Keys.DateTimePk.Format(DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified)), () => KeyFormats.Keys.DateTimePk.TryOf(DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified)) is not null),
        new("custom type", true, () => KeyFormats.Keys.CustomPk.TryOf(IPAddress.Loopback, out _), () => KeyFormats.Keys.CustomPk.Format(IPAddress.Loopback), () => KeyFormats.Keys.CustomPk.TryOf(IPAddress.Loopback) is not null),
    ];

    public static IEnumerable<KeyCase> PrimaryKeys() =>
    [
        new("both keys fit", true, () => Member.Keys.TryOf("t", "1", "EU", "Bern", out _), () => Member.Keys.Of("t", "1", "EU", "Bern"), () => Member.Keys.TryOf("t", "1", "EU", "Bern") is not null),
        new("the partition key does not", false, () => Member.Keys.TryOf("a#b", "1", "EU", "Bern", out _), () => Member.Keys.Of("a#b", "1", "EU", "Bern"), () => Member.Keys.TryOf("a#b", "1", "EU", "Bern") is not null),
        new("the sort key does not", false, () => Member.Keys.TryOf("t", "1", "EU", "a#b", out _), () => Member.Keys.Of("t", "1", "EU", "a#b"), () => Member.Keys.TryOf("t", "1", "EU", "a#b") is not null),
        new("empty sort key value", false, () => Member.Keys.TryOf("t", "1", "EU", string.Empty, out _), () => Member.Keys.Of("t", "1", "EU", string.Empty), () => Member.Keys.TryOf("t", "1", "EU", string.Empty) is not null),
        new("beside a plain key", true, () => Account.Keys.TryOf("a", 7, out _), () => Account.Keys.Of("a", 7), () => Account.Keys.TryOf("a", 7) is not null),
        new("a value wider than its format", false, () => Account.Keys.TryOf("a", 1000, out _), () => Account.Keys.Of("a", 1000), () => Account.Keys.TryOf("a", 1000) is not null),
        new("a value a plain key takes and the template beside it does not", false, () => Account.Keys.TryOf("a#b", 7, out _), () => Account.Keys.Of("a#b", 7), () => Account.Keys.TryOf("a#b", 7) is not null),
        new("raw", true, () => RawKeyed.Keys.TryOf("a#b", "hq", new DateOnly(2026, 11, 2), "x#y", out _), () => RawKeyed.Keys.Of("a#b", "hq", new DateOnly(2026, 11, 2), "x#y"), () => RawKeyed.Keys.TryOf("a#b", "hq", new DateOnly(2026, 11, 2), "x#y") is not null),
        new("a strict field beside a raw one", false, () => RawKeyed.Keys.TryOf("a#b", "h#q", new DateOnly(2026, 11, 2), "x#y", out _), () => RawKeyed.Keys.Of("a#b", "h#q", new DateOnly(2026, 11, 2), "x#y"), () => RawKeyed.Keys.TryOf("a#b", "h#q", new DateOnly(2026, 11, 2), "x#y") is not null),
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
        var isKey = Member.Keys.Pk.TryOf("t", "1", out var key);

        // Assert
        await Assert.That((isKey, key.Value, key.AttributeName)).IsEqualTo((true, expected.Value, expected.AttributeName));
    }

    [Test]
    public async Task TryOf_ValueThatDoesNotFit_LeavesTheDefault()
    {
        // Arrange, Act
        var isKey = Member.Keys.Pk.TryOf("a#b", "1", out var key);

        // Assert
        await Assert.That((isKey, key.Value)).IsEqualTo((false, string.Empty));
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
        _ = Member.Keys.TryOf("t", "1", "EU", "Bern", out var key);

        // Assert
        await Assert.That(members.Get(key).ToRequestJson()).IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task TryOfNullable_ValueOfATable_AgreesWithTheOutForm(KeyCase row)
    {
        // Arrange (the rows of the out form, through the builder of the same key without the out parameter)
        var expected = row.Try();

        // Act
        var found = row.TryNullable();

        // Assert
        await Assert.That(found).IsEqualTo(expected);
    }

    [Test]
    public async Task TryOfNullable_KeyThatFits_IsTheKeyOf()
    {
        // Arrange
        var expected = Member.Keys.Pk.Of("t", "1");

        // Act
        var key = Member.Keys.Pk.TryOf("t", "1");

        // Assert
        await Assert.That(key is { } found ? (found.Value, found.AttributeName) : default).IsEqualTo((expected.Value, expected.AttributeName));
    }

    [Test]
    public async Task TryOfNullable_ValueThatDoesNotFit_IsNull()
    {
        // Arrange, Act
        var key = Member.Keys.Pk.TryOf("a#b", "1");

        // Assert
        await Assert.That(key.HasValue).IsFalse();
    }

    [Test]
    public async Task TryOfNullable_KeyOfAnIndex_IsTheKeyOfItsBuilder()
    {
        // Arrange
        var expected = RawKeyed.Keys.ByOwnerPk.Of("alice");

        // Act
        var key = RawKeyed.Keys.ByOwnerPk.TryOf("alice");

        // Assert
        await Assert.That(key is { } found ? (found.Value, found.AttributeName) : default).IsEqualTo((expected.Value, expected.AttributeName));
    }

    [Test]
    public async Task TryOfNullable_KeyOfAnIndexThatDoesNotFit_IsNull()
    {
        // Arrange, Act
        var key = RawKeyed.Keys.ByOwnerPk.TryOf("a#b");

        // Assert
        await Assert.That(key.HasValue).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(PrimaryKeys))]
    public async Task TryOfNullable_ValuesOfAPrimaryKey_AgreeWithTheOutForm(KeyCase row)
    {
        // Arrange
        var expected = row.Try();

        // Act
        var found = row.TryNullable();

        // Assert
        await Assert.That(found).IsEqualTo(expected);
    }

    [Test]
    public async Task TryOfNullable_PrimaryKeyThatFits_WritesWhatOfWrites()
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
    public async Task TryOfNullable_PrimaryKeyThatDoesNotFit_IsNull()
    {
        // Arrange, Act
        var key = Member.Keys.TryOf("t", "1", "EU", "a#b");

        // Assert
        await Assert.That(key.HasValue).IsFalse();
    }

    [Test]
    public async Task TryOfNullable_PlaceholderCalledKey_StillTakesItsValue()
    {
        // Arrange, Act
        var result = RawKeyed.Keys.Pk.TryOf(key: "a#b");

        // Assert
        await Assert.That(result?.Value).IsEqualTo("APPOINTMENT#a#b");
    }

    [Test]
    public async Task TryOfNullable_PrimaryKey_AllocatesNothing()
    {
        // Arrange (a nullable struct is no box)
        _ = Member.Keys.TryOf("t", "1", "EU", "Bern");
        _ = Member.Keys.TryOf("t", "a#b", "EU", "Bern");

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = Member.Keys.TryOf("t", "1", "EU", "Bern");
            _ = Member.Keys.TryOf("t", "a#b", "EU", "Bern");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task TryOf_PlaceholderCalledKey_StillTakesItsValue()
    {
        // Arrange, Act
        var isKey = RawKeyed.Keys.Pk.TryOf(key: "a#b", key_: out var result);

        // Assert
        await Assert.That((isKey, result.Value)).IsEqualTo((true, "APPOINTMENT#a#b"));
    }

    [Test]
    public async Task TryOf_ValueThatDoesNotFit_AllocatesNothing()
    {
        // Arrange (a first pass compiles what the second measures)
        const string Value = "a#b";
        _ = Member.Keys.Pk.TryOf(Value, "1", out _);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = Member.Keys.Pk.TryOf(Value, "1", out _);
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
        var tryOf = Measure(static () =>
        {
            _ = Member.Keys.Pk.TryOf("t", "1", out var key);
            return key.Value.Length;
        });

        // Assert
        await Assert.That(tryOf).IsEqualTo(of);
    }

    [Test]
    public async Task OfAndTryOf_PrimaryKey_AllocateNothing()
    {
        // Arrange (what a span scan costs is the claim: the values go through the stack and no string comes of it)
        _ = Member.Keys.Of("t", "1", "EU", "Bern");
        _ = Member.Keys.TryOf("t", "a#b", "EU", "Bern", out _);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = Member.Keys.Of("t", "1", "EU", "Bern");
            _ = Member.Keys.TryOf("t", "1", "EU", "Bern", out _);
            _ = Member.Keys.TryOf("t", "a#b", "EU", "Bern", out _);
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
