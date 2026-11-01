using System.Net;
using System.Reflection;
using System.Text;
using DynaSharp.Mapping;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>
/// The generated key builders, run: expected keys are written by hand from the key grammar, never derived from the writer. The entities are
/// in <c>KeyModels.cs</c>.
/// </summary>
public sealed class GeneratedKeysTests
{
    private const string UserText = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private const string OrderText = "4f9c2a7e-0b1d-4e5f-8a6b-7c8d9e0f1a2b";

    private const string OrderKey = "ORDER#2026-10-31T12:34:56.789Z#" + OrderText;

    private static readonly Guid User = Guid.Parse(UserText);

    private static readonly Guid OrderId = Guid.Parse(OrderText);

    // 14:34 at +02:00, so that a format that kept the offset would show.
    private static readonly DateTimeOffset At = new(2026, 10, 31, 14, 34, 56, 789, TimeSpan.FromHours(2));

    private static readonly DateTimeOffset AtWithTicks = At.AddTicks(1234);

    private static readonly DateTime Utc = new(2026, 10, 31, 12, 34, 56, 789, DateTimeKind.Utc);

    // Delegates of their own: the test framework calls a Func from a data source to get the value of the row.
    public delegate string? KeyFormatCall();

    public delegate bool KeyParseCall();

    public static IEnumerable<KeyFormatCall> InvalidValueFormats() =>
    [
        () => Member.Keys.Pk.Format("a#b", "1"),
        () => Member.Keys.Pk.Format("a", "1#USER#2"),
        () => Member.Keys.Sk.Format("EU", "#"),
        () => Member.Keys.Pk.Format(string.Empty, "1"),
        () => Member.Keys.Pk.Format("a", string.Empty),
        () => Member.Keys.Pk.Format(null!, "1"),
        () => Member.Keys.Pk.Format("a\u0001", "1"),
        () => Member.Keys.Pk.Format("a\U0010FFFF", "1"),
        () => Member.Keys.Pk.Format("\uD800", "1"),
        () => Member.Keys.Sk.Prefix("a#b"),
        () => Member.Keys.Sk.Prefix(string.Empty),
        () => Member.Keys.Sk.UpperBound("a#b"),
        () => Member.Keys.Sk.PrefixPartial("EU", "a#b"),
        () => Member.Keys.Sk.PrefixPartial("EU", string.Empty),
        () => Member.Keys.Sk.PrefixPartial("E#U", "a"),
        () => Suffixed.Keys.Sk.Format("a#b"),
        () => Suffixed.Keys.Sk.Format("a-b"),
        () => Suffixed.Keys.Sk.Prefix("a-b"),
        () => Awkward.Keys.Pk.Format("a{b"),
        () => Awkward.Keys.Pk.Format("a}b"),
        () => Adjacent.Keys.Sk.Format(1000, "a"),
        () => KeyFormats.Keys.DigitsIntPk.Format(-1),
        () => KeyFormats.Keys.FixedDecimalPk.Format(1.234m),
        () => KeyFormats.Keys.FixedDecimalPk.Format(10000m),
        () => KeyFormats.Keys.DateTimePk.Format(DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified)),
    ];

    public static IEnumerable<KeyParseCall> RejectedTexts() =>
    [
        () => KeyFormats.Keys.SbytePk.TryParse("I#128", out _),
        () => KeyFormats.Keys.SbytePk.TryParse("I#-129", out _),
        () => KeyFormats.Keys.BytePk.TryParse("I#256", out _),
        () => KeyFormats.Keys.BytePk.TryParse("I#-1", out _),
        () => KeyFormats.Keys.ShortPk.TryParse("I#32768", out _),
        () => KeyFormats.Keys.ShortPk.TryParse("I#-32769", out _),
        () => KeyFormats.Keys.UshortPk.TryParse("I#65536", out _),
        () => KeyFormats.Keys.IntPk.TryParse("I#2147483648", out _),
        () => KeyFormats.Keys.IntPk.TryParse("I#-2147483649", out _),
        () => KeyFormats.Keys.UintPk.TryParse("I#4294967296", out _),
        () => KeyFormats.Keys.LongPk.TryParse("I#9223372036854775808", out _),
        () => KeyFormats.Keys.UlongPk.TryParse("I#18446744073709551616", out _),
        () => KeyFormats.Keys.IntPk.TryParse("I#+1", out _),
        () => KeyFormats.Keys.IntPk.TryParse("I#01", out _),
        () => KeyFormats.Keys.DigitsBytePk.TryParse("D#0256", out _),
        () => KeyFormats.Keys.DigitsBytePk.TryParse("D#255", out _),
        () => KeyFormats.Keys.DigitsIntPk.TryParse("D#002147483648", out _),
        () => KeyFormats.Keys.ByteEnumPk.TryParse("E#256", out _),
        () => KeyFormats.Keys.FixedIntPk.TryParse("F#P002147483648", out _),
        () => KeyFormats.Keys.FixedIntPk.TryParse("F#N997852516350", out _),
        () => KeyFormats.Keys.FixedULongPk.TryParse("F#N99999999999999999998", out _),
        () => KeyFormats.Keys.NamePk.TryParse("E#Unknown", out _),
        () => KeyFormats.Keys.NamePk.TryParse("E#pro", out _),
        () => KeyFormats.Keys.DecimalPk.TryParse("M#1.50", out _),
        () => KeyFormats.Keys.FixedDecimalPk.TryParse("M#N9999.99", out _),
        () => KeyFormats.Keys.GuidPk.TryParse("G#" + OrderText.ToUpperInvariant(), out _),
        () => KeyFormats.Keys.MonthPk.TryParse("T#2026-13", out _),
        () => KeyFormats.Keys.DatePk.TryParse("T#2026-02-30", out _),
        () => KeyFormats.Keys.CustomPk.TryParse("C#010.0.0.1", out _),
        () => Member.Keys.Pk.TryParse("TENANT#a\uD800#USER#1", out _),
        () => Member.Keys.Pk.TryParse((string?)null, out _),
        () => Member.Keys.Pk.TryParse(string.Empty, out _),
        () => Member.Keys.Pk.TryParse("TENANT#a#USER#1\U0010FFFF", out _),
        () => UserProfile.Keys.Sk.TryParse("PROFILE#", out _),
        () => Suffixed.Keys.Sk.TryParse("A-x#", out _),
        () => Adjacent.Keys.Sk.TryParse("07ab", out _),
    ];

    [Test]
    public async Task Format_CompositeSortKey_WritesTheTextOfTheGrammar()
    {
        // Arrange, Act
        var key = KeyedOrder.Keys.Sk.Format(At, OrderId);

        // Assert
        await Assert.That(key).IsEqualTo(OrderKey);
    }

    [Test]
    public async Task Format_NamedArguments_BindByPropertyNameInAnyOrder()
    {
        // Arrange, Act (two parameters of one type, which is what the names guard)
        var key = Member.Keys.Pk.Format(id: "7", tenant: "acme");

        // Assert
        await Assert.That(key).IsEqualTo("TENANT#acme#USER#7");
    }

    [Test]
    public async Task Format_OfEveryKeyOfAnEntity_WritesItsTemplate()
    {
        // Arrange
        string[] expected =
        [
            "USER#" + UserText, OrderKey, "STATUS#Pro", "P0000000012.50", "TRACK#" + OrderText, "2026-10-31T12:34:56.789Z", "TOTAL#N9999999987.49",
        ];

        // Act
        string[] keys =
        [
            KeyedOrder.Keys.Pk.Format(User), KeyedOrder.Keys.Sk.Format(At, OrderId), KeyedOrder.Keys.Gsi1Pk.Format(Plan.Pro), KeyedOrder.Keys.Gsi1Sk.Format(12.5m),
            KeyedOrder.Keys.Gsi2Pk.Format(OrderId), KeyedOrder.Keys.Gsi2Sk.Format(At), KeyedOrder.Keys.Lsi1Sk.Format(-12.5m),
        ];

        // Assert
        await Assert.That(keys).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Format_Strings_KeepsTextAsItIs()
    {
        // Arrange, Act
        string[] keys = [KeyFormats.Keys.Pk.Format("a#b"), Member.Keys.Sk.Format("EU", "Zürich"), Member.Keys.Sk.Format("a b", "c")];

        // Assert (a template of one placeholder has no delimiter, and values are never folded)
        await Assert.That(keys).IsEquivalentTo(["a#b", "REGION#EU#CITY#Zürich", "REGION#a b#CITY#c"]);
    }

    [Test]
    public async Task Format_EveryIntegerType_WritesPlainDigits()
    {
        // Arrange
        string[] expected =
        [
            "I#-128", "I#255", "I#-32768", "I#65535", "I#-2147483648", "I#4294967295", "I#-9223372036854775808", "I#18446744073709551615", "I#0",
        ];

        // Act
        string[] keys =
        [
            KeyFormats.Keys.SbytePk.Format(sbyte.MinValue), KeyFormats.Keys.BytePk.Format(byte.MaxValue), KeyFormats.Keys.ShortPk.Format(short.MinValue),
            KeyFormats.Keys.UshortPk.Format(ushort.MaxValue), KeyFormats.Keys.IntPk.Format(int.MinValue), KeyFormats.Keys.UintPk.Format(uint.MaxValue),
            KeyFormats.Keys.LongPk.Format(long.MinValue), KeyFormats.Keys.UlongPk.Format(ulong.MaxValue), KeyFormats.Keys.PlainPk.Format(0),
        ];

        // Assert
        await Assert.That(keys).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Format_FixedFormats_WritesEncodingsThatSortByValue()
    {
        // Arrange
        string[] expected =
        [
            "D#000000000042", "D#0255", "D#18446744073709551615", "F#P000000000042", "F#N999999999957", "F#P18446744073709551615", "E#001", "E#200", "M#P0012.50", "M#N9987.49",
            "M#P0000.00",
        ];

        // Act
        string[] keys =
        [
            KeyFormats.Keys.DigitsIntPk.Format(42), KeyFormats.Keys.DigitsBytePk.Format(byte.MaxValue), KeyFormats.Keys.DigitsULongPk.Format(ulong.MaxValue),
            KeyFormats.Keys.FixedIntPk.Format(42), KeyFormats.Keys.FixedIntPk.Format(-42), KeyFormats.Keys.FixedULongPk.Format(ulong.MaxValue),
            KeyFormats.Keys.ValuePk.Format(Plan.Pro), KeyFormats.Keys.ByteEnumPk.Format(Tier.High), KeyFormats.Keys.FixedDecimalPk.Format(12.5m),
            KeyFormats.Keys.FixedDecimalPk.Format(-12.5m), KeyFormats.Keys.FixedDecimalPk.Format(-0.00m),
        ];

        // Assert
        await Assert.That(keys).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Format_PlainDecimal_NormalizesEqualNumbersToOneKey()
    {
        // Arrange, Act
        string[] keys = [KeyFormats.Keys.DecimalPk.Format(1.50m), KeyFormats.Keys.DecimalPk.Format(1m), KeyFormats.Keys.DecimalPk.Format(1.0m), KeyFormats.Keys.DecimalPk.Format(-0.0m)];

        // Assert
        await Assert.That(keys).IsEquivalentTo(["M#1.5", "M#1", "M#1", "M#0"]);
    }

    [Test]
    public async Task Format_EveryTimestampFormat_TruncatesToItsWidthInUtc()
    {
        // Arrange
        string[] expected =
        [
            "T#2026", "T#2026-10", "T#2026-10-31", "T#2026-10-31T12", "T#2026-10-31T12:34", "T#2026-10-31T12:34:56Z", "T#2026-10-31T12:34:56.789Z",
            "T#2026-10-31T12:34:56.789123Z", "T#2026-10-31T12:34:56.7891234Z",
        ];

        // Act
        string[] keys =
        [
            KeyFormats.Keys.YearPk.Format(AtWithTicks), KeyFormats.Keys.MonthPk.Format(AtWithTicks), KeyFormats.Keys.DayPk.Format(AtWithTicks), KeyFormats.Keys.HourPk.Format(AtWithTicks),
            KeyFormats.Keys.MinutePk.Format(AtWithTicks), KeyFormats.Keys.SecondPk.Format(AtWithTicks), KeyFormats.Keys.MilliPk.Format(AtWithTicks), KeyFormats.Keys.MicroPk.Format(AtWithTicks),
            KeyFormats.Keys.TicksPk.Format(AtWithTicks),
        ];

        // Assert
        await Assert.That(keys).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Format_OtherTypes_WritesTheirText()
    {
        // Arrange, Act
        string[] keys =
        [
            KeyFormats.Keys.DateTimePk.Format(Utc), KeyFormats.Keys.DateTimeTicksPk.Format(Utc.AddTicks(1)), KeyFormats.Keys.DatePk.Format(new DateOnly(2026, 10, 31)),
            KeyFormats.Keys.CustomPk.Format(IPAddress.Parse("10.0.0.1")), KeyFormats.Keys.GuidPk.Format(OrderId), KeyFormats.Keys.NamePk.Format(Plan.Enterprise),
        ];

        // Assert
        await Assert.That(keys).IsEquivalentTo(
        [
            "T#2026-10-31T12:34:56.789Z", "T#2026-10-31T12:34:56.7890001Z", "T#2026-10-31", "C#10.0.0.1", "G#" + OrderText, "E#Enterprise",
        ]);
    }

    [Test]
    public async Task Format_LiteralsOnly_TakesNoParameters()
    {
        // Arrange, Act
        string[] keys = [UserProfile.Keys.Sk.Format(), SiteSettings.Keys.Pk.Format(), SiteSettings.Keys.Sk.Format()];

        // Assert
        await Assert.That(keys).IsEquivalentTo(["PROFILE", "CONFIG", "GLOBAL"]);
    }

    [Test]
    public async Task Format_AValueLongerThanTheStackBuffer_GoesThroughThePool()
    {
        // Arrange
        var id = new string('x', 1000);

        // Act
        var key = Member.Keys.Pk.Format("t", id);
        var parts = Member.Keys.Pk.Parse(key);

        // Assert
        await Assert.That((key, parts.Id)).IsEqualTo(("TENANT#t#USER#" + id, id));
    }

    [Test]
    public async Task Format_TemplateThatNeedsEscaping_KeepsItsLiteralsAndDelimiters()
    {
        // Arrange, Act
        var key = Awkward.Keys.Pk.Format("x");

        // Assert
        await Assert.That((key, Awkward.Keys.Pk.Parse(key).Id)).IsEqualTo(("Q\"\\<&{x}", "x"));
    }

    [Test]
    public async Task Format_LiteralsWithBraces_WritesTheBracesOnce()
    {
        // Arrange, Act
        var key = Awkward.Keys.Sk.Format();

        // Assert
        await Assert.That((key, KeyJson(Awkward.Keys.Of("x")), Awkward.Keys.Sk.TryParse(key, out _)))
            .IsEqualTo(("L{I}T", """{"pk":{"S":"Q\"\\<&{x}"},"sk":{"S":"L{I}T"}}""", true));
    }

    [Test]
    public async Task Format_KeywordsAsNames_BindAsParametersAndParts()
    {
        // Arrange
        var key = Keywords.Keys.Pk.Format(@class: "a");

        // Act
        var parts = Keywords.Keys.Pk.Parse(key);

        // Assert
        await Assert.That((key, parts.@class)).IsEqualTo(("C#a", "a"));
    }

    [Test]
    public async Task Format_ComputedInput_IsAParameterLikeAnyOther()
    {
        // Arrange
        var article = new Article { Title = "a b" };

        // Act
        var key = Article.Keys.Pk.Format(article.Slug);

        // Assert
        await Assert.That((key, Article.Keys.Pk.Parse(key).Slug)).IsEqualTo(("SLUG#a-b", "a-b"));
    }

    [Test]
    public async Task Format_EnumOnlyInAKey_ParsesBackToTheEnum()
    {
        // Arrange
        var key = KeyOnlyPlan.Keys.Pk.Format(Plan.Pro);

        // Act
        var parts = KeyOnlyPlan.Keys.Pk.Parse(key);

        // Assert (the read path of [KeyOnly] restores the property from these parts)
        await Assert.That((key, parts.Plan)).IsEqualTo(("PLAN#Pro", Plan.Pro));
    }

    [Test]
    public async Task Format_DerivedEntity_HasItsOwnKeys()
    {
        // Arrange, Act
        string[] keys = [BaseEvent.Keys.Pk.Format("1"), AuditEvent.Keys.Pk.Format("1")];

        // Assert
        await Assert.That(keys).IsEquivalentTo(["EVENT#1", "AUDIT#1"]);
    }

    [Test]
    public async Task Format_EntityInsideOtherTypes_HasItsKeys()
    {
        // Arrange, Act
        var key = Outer.Inner.Point.Keys.Pk.Format(-3);

        // Assert
        await Assert.That(key).IsEqualTo("POINT#-3");
    }

    [Test]
    public async Task Format_KeysThatShareAnAttribute_HaveBuildersToo()
    {
        // Arrange, Act
        string[] keys = [Edge.Keys.Pk.Format("a"), Edge.Keys.Sk.Format("b"), Edge.Keys.InvertedPk.Format("b"), Edge.Keys.InvertedSk.Format("a")];

        // Assert
        await Assert.That(keys).IsEquivalentTo(["FROM#a", "TO#b", "TO#b", "FROM#a"]);
    }

    [Test]
    public async Task Format_SparseInput_TakesTheValueNotTheNullableProperty()
    {
        // Arrange
        var order = new KeyedOrder { UserId = User, CreatedAt = At, OrderId = OrderId, Status = Plan.Free, Total = 1m, TrackingId = OrderId };

        // Act (the emitter of the item leaves the attribute out when the property is null)
        var key = order.TrackingId is { } tracking ? KeyedOrder.Keys.Gsi2Pk.Format(tracking) : null;

        // Assert
        await Assert.That(key).IsEqualTo("TRACK#" + OrderText);
    }

    [Test]
    [MethodDataSource(nameof(InvalidValueFormats))]
    public async Task Format_AValueThatCannotBePartOfAKey_ThrowsKeyFormatException(KeyFormatCall format)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(format);

        // Act
        var act = () => format();

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>();
    }

    [Test]
    [Arguments("s", null, """{"pk":{"S":"a"},"shared":{"S":"TENANT#t"},"bysellersk":{"S":"SELLER#s"},"_type":{"S":"GATED"},"Id":{"S":"a"},"Tenant":{"S":"t"},"Seller":{"S":"s"}}""")]
    [Arguments(null, "b", """{"pk":{"S":"a"},"shared":{"S":"TENANT#t"},"bybuyersk":{"S":"BUYER#b"},"_type":{"S":"GATED"},"Id":{"S":"a"},"Tenant":{"S":"t"},"Buyer":{"S":"b"}}""")]
    [Arguments("s", "b", """{"pk":{"S":"a"},"shared":{"S":"TENANT#t"},"bysellersk":{"S":"SELLER#s"},"bybuyersk":{"S":"BUYER#b"},"_type":{"S":"GATED"},"Id":{"S":"a"},"Tenant":{"S":"t"},"Seller":{"S":"s"},"Buyer":{"S":"b"}}""")]
    [Arguments(null, null, """{"pk":{"S":"a"},"_type":{"S":"GATED"},"Id":{"S":"a"},"Tenant":{"S":"t"}}""")]
    public async Task WriteAttributes_SharedAttributeUnderTwoGates_IsWrittenWhenEitherIsOpen(string? seller, string? buyer, string expected)
    {
        // Arrange
        var sale = new GatedSale { Id = "a", Tenant = "t", Seller = seller, Buyer = buyer };

        // Act
        var item = EntityJson.Write(sale);

        // Assert
        await Assert.That(item).IsEqualTo(expected);
    }

    [Test]
    [Arguments("n", 7, """{"pk":{"S":"a"},"shared":{"S":"NICK#n"},"bynickandagesk":{"S":"007"},"_type":{"S":"NICK"},"Id":{"S":"a"},"Nick":{"S":"n"},"Age":{"N":"7"}}""")]
    [Arguments("n", null, """{"pk":{"S":"a"},"shared":{"S":"NICK#n"},"_type":{"S":"NICK"},"Id":{"S":"a"},"Nick":{"S":"n"}}""")]
    [Arguments(null, 7, """{"pk":{"S":"a"},"_type":{"S":"NICK"},"Id":{"S":"a"},"Age":{"N":"7"}}""")]
    public async Task WriteAttributes_SharedAttributeWhoseInputIsTheWholeGateOfOneIndex_FollowsThatInput(string? nick, int? age, string expected)
    {
        // Arrange
        var name = new NickName { Id = "a", Nick = nick, Age = age };

        // Act
        var item = EntityJson.Write(name);

        // Assert
        await Assert.That(item).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null, """{"pk":{"S":"a"},"shared":{"S":"T#t"},"openpk":{"S":"O#a"},"_type":{"S":"LOOSE"},"Id":{"S":"a"},"Tenant":{"S":"t"}}""")]
    [Arguments("n", """{"pk":{"S":"a"},"shared":{"S":"T#t"},"openpk":{"S":"O#a"},"gatedpk":{"S":"G#n"},"_type":{"S":"LOOSE"},"Id":{"S":"a"},"Tenant":{"S":"t"},"Note":{"S":"n"}}""")]
    public async Task WriteAttributes_SharedSortKeyWithAnIndexWithoutAGate_IsAlwaysWritten(string? note, string expected)
    {
        // Arrange
        var loose = new LooseGate { Id = "a", Tenant = "t", Note = note };

        // Act
        var item = EntityJson.Write(loose);

        // Assert
        await Assert.That(item).IsEqualTo(expected);
    }

    [Test]
    public async Task Format_UndefinedEnumByName_ThrowsKeyFormatExceptionNamingThePlaceholderAndNotTheValue()
    {
        // Arrange
        const Plan Value = (Plan)4242;

        // Act
        var exception = Assert.Throws<KeyFormatException>(() => KeyedOrder.Keys.Gsi1Pk.Format(Value));

        // Assert
        await Assert.That((exception.ParamName, exception.Message.Contains("4242", StringComparison.Ordinal))).IsEqualTo(("Status", false));
    }

    [Test]
    public async Task WriteAttributes_UndefinedEnumInAKey_ThrowsKeyFormatException()
    {
        // Arrange
        var order = new KeyedOrder { UserId = User, CreatedAt = At, OrderId = OrderId, Status = (Plan)4242, Total = 1m };

        // Act
        var act = () => EntityJson.Write(order);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>();
    }

    [Test]
    public async Task Format_DelimiterInTheLastPlaceholder_NamesThePlaceholderAndNotTheValue()
    {
        // Arrange
        const string Value = "1#ORDER";

        // Act
        var exception = Assert.Throws<KeyFormatException>(() => Member.Keys.Pk.Format("t", Value));

        // Assert
        await Assert.That((exception.ParamName, exception.Message.Contains(Value, StringComparison.Ordinal))).IsEqualTo(("Id", false));
    }

    [Test]
    public async Task Prefix_EveryDepth_EndsOnADelimiter()
    {
        // Arrange, Act
        string[] prefixes = [KeyedOrder.Keys.Sk.Prefix(), KeyedOrder.Keys.Sk.Prefix(At), Member.Keys.Sk.Prefix(), Member.Keys.Sk.Prefix("EU")];

        // Assert
        await Assert.That(prefixes).IsEquivalentTo(["ORDER#", "ORDER#2026-10-31T12:34:56.789Z#", "REGION#", "REGION#EU#CITY#"]);
    }

    [Test]
    public async Task Prefix_LiteralAfterTheLastPlaceholder_KeepsOnlyItsDelimiter()
    {
        // Arrange, Act
        string[] prefixes = [Suffixed.Keys.Sk.Prefix(), Suffixed.Keys.Sk.Prefix("x")];

        // Assert
        await Assert.That(prefixes).IsEquivalentTo(["A-", "A-x#"]);
    }

    [Test]
    public async Task Prefix_PlaceholdersWithoutALiteralBetween_HasNoDepthThere()
    {
        // Arrange, Act (the first field of {Code:d3}{Name} ends on no delimiter)
        var overloads = typeof(Adjacent.Keys.Sk).GetMethods().Count(method => method.Name == nameof(KeyedOrder.Keys.Sk.Prefix));

        // Assert
        await Assert.That(overloads).IsEqualTo(0);
    }

    [Test]
    public async Task Prefix_PartitionKey_DoesNotExist()
    {
        // Arrange, Act (begins_with and ranges are for sort keys)
        var methods = typeof(KeyedOrder.Keys.Pk).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name).Distinct();

        // Assert
        await Assert.That(methods).IsEquivalentTo(["Format", "Of", "Parse", "TryParse"]);
    }

    [Test]
    public async Task Of_EveryPartitionKey_TakesWhatFormatTakes()
    {
        // Arrange
        var entities = typeof(KeyedOrder).Assembly.GetTypes().Where(static type => type.Namespace == typeof(KeyedOrder).Namespace && type.GetNestedType("Keys") is not null);
        var builders = entities.SelectMany(static entity => entity.GetNestedType("Keys")!.GetNestedTypes().Where(static type => type.GetMethod("Of") is not null && type.GetMethod("Format") is not null)).ToList();

        // Act
        var differing = builders.Where(static builder => !Parameters(builder, "Of").SequenceEqual(Parameters(builder, "Format"))).ToList();

        // Assert
        await Assert.That((builders.Count > 20, differing.Count)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Of_BadValue_ThrowsAsFormatDoes()
    {
        // Arrange
        const string Delimited = "a#b";

        // Act
        var exception = Assert.Throws<KeyFormatException>(() => Member.Keys.Pk.Of("t", Delimited));

        // Assert
        await Assert.That((exception.ParamName, exception.Message.Contains(Delimited, StringComparison.Ordinal))).IsEqualTo(("Id", false));
    }

    [Test]
    public async Task Of_KeyOfLiterals_TakesNothing()
    {
        // Arrange, Act
        var key = FeaturedPost.Keys.EverythingPk.Of();

        // Assert
        await Assert.That(key.Value).IsEqualTo("ALL");
    }

    [Test]
    public async Task BeginsWith_EveryDepth_MatchesThePrefix()
    {
        // Arrange
        using var handler = new DynaSharp.Testing.StubHandler();
        using var client = TestClient.Create(handler);
        var orders = client.Table<KeyedOrder>("Shop");
        var key = KeyedOrder.Keys.Pk.Of(default);

        // Act
        var whole = orders.Query(key, KeyedOrder.Keys.Sk.BeginsWith()).ToRequestJson();
        var deeper = orders.Query(key, KeyedOrder.Keys.Sk.BeginsWith(At)).ToRequestJson();

        // Assert
        await Assert.That((whole.Contains("""{"S":"ORDER#"}""", StringComparison.Ordinal), deeper.Contains("""{"S":"ORDER#2026-10-31T12:34:56.789Z#"}""", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task BeginsWith_LiteralAfterTheLastPlaceholder_KeepsOnlyItsDelimiter()
    {
        // Arrange
        using var handler = new DynaSharp.Testing.StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk", "sk");

        // Act
        var json = table.Query("a", Suffixed.Keys.Sk.BeginsWith("x")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""{"S":"A-x#"}""");
    }

    [Test]
    public async Task BeginsWith_BadValue_ThrowsAsPrefixDoes()
    {
        // Arrange
        const string Delimited = "a#b";

        // Act
        var exception = Assert.Throws<KeyFormatException>(() => Member.Keys.Sk.BeginsWith(Delimited));

        // Assert
        await Assert.That(exception.ParamName).IsEqualTo("Region");
    }

    [Test]
    public async Task Builders_LocalIndex_HasOnlyItsSortKey()
    {
        // Arrange, Act
        var builders = typeof(KeyedOrder.Keys).GetNestedTypes().Where(type => type.IsAbstract).Select(type => type.Name);

        // Assert
        await Assert.That(builders).IsEquivalentTo(["Pk", "Sk", "Gsi1Pk", "Gsi1Sk", "Gsi2Pk", "Gsi2Sk", "Lsi1Sk"]);
    }

    [Test]
    public async Task Builders_PlainKeys_HaveOneEach()
    {
        // Arrange, Act
        var builders = typeof(Device.Keys).GetNestedTypes().Where(type => type.IsAbstract).Select(static type => type.Name).Order(StringComparer.Ordinal);

        // Assert
        await Assert.That(builders).IsEquivalentTo(["Pk", "Sk"]);
    }

    [Test]
    public async Task Builders_PlainKeyStoredAsANumber_HasOfAlone()
    {
        // Arrange, Act
        var methods = typeof(NumberKeyed.Keys.Pk).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(static method => method.Name);

        // Assert
        await Assert.That(methods).IsEquivalentTo(["Of"]);
    }

    [Test]
    public async Task UpperBound_EveryDepth_IsThePrefixPlusTheHighestScalar()
    {
        // Arrange, Act
        string[] bounds = [Member.Keys.Sk.UpperBound(), Member.Keys.Sk.UpperBound("EU"), KeyedOrder.Keys.Sk.UpperBound(At), Suffixed.Keys.Sk.UpperBound("x")];

        // Assert
        await Assert.That(bounds).IsEquivalentTo(
        [
            "REGION#\U0010FFFF", "REGION#EU#CITY#\U0010FFFF", "ORDER#2026-10-31T12:34:56.789Z#\U0010FFFF", "A-x#\U0010FFFF",
        ]);
    }

    [Test]
    public async Task UpperBound_FixedWidthTemplate_WritesTheWholeBoundEvenIfTheBufferIsExact()
    {
        // Arrange (the prefix through the last field is nearly the whole key, and the bound is four bytes longer)
        var key = Ledger.Keys.Sk.Format(At, OrderId);

        // Act
        var bound = Ledger.Keys.Sk.UpperBound(At);

        // Assert
        await Assert.That(bound.StartsWith(key[..^OrderText.Length], StringComparison.Ordinal) && bound.EndsWith("\U0010FFFF", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task PrefixPartial_LastStringPlaceholder_StopsInsideTheValue()
    {
        // Arrange, Act
        string[] prefixes = [Member.Keys.Sk.PrefixPartial("EU", "Zü"), Suffixed.Keys.Sk.PrefixPartial("x"), Adjacent.Keys.Sk.PrefixPartial(7, "a")];

        // Assert
        await Assert.That(prefixes).IsEquivalentTo(["REGION#EU#CITY#Zü", "A-x", "007a"]);
    }

    [Test]
    public async Task PrefixPartial_LastPlaceholderIsNotAString_DoesNotExist()
    {
        // Arrange, Act
        var method = typeof(KeyedOrder.Keys.Sk).GetMethod("PrefixPartial");

        // Assert
        await Assert.That(method).IsNull();
    }

    [Test]
    public async Task Parse_Text_GivesTheValuesAsPartsWithPropertyNames()
    {
        // Arrange, Act
        var parts = KeyedOrder.Keys.Sk.Parse(OrderKey);

        // Assert
        await Assert.That(parts).IsEqualTo(new KeyedOrder.Keys.SkParts(At, OrderId));
    }

    [Test]
    public async Task Parse_Utf8_GivesTheSameParts()
    {
        // Arrange
        var utf8 = Encoding.UTF8.GetBytes(OrderKey);

        // Act
        var parts = KeyedOrder.Keys.Sk.Parse(utf8);
        var tried = KeyedOrder.Keys.Sk.TryParse(utf8, out var triedParts);

        // Assert
        await Assert.That((parts, tried, triedParts)).IsEqualTo((new KeyedOrder.Keys.SkParts(At, OrderId), true, new KeyedOrder.Keys.SkParts(At, OrderId)));
    }

    [Test]
    public async Task Parse_LiteralsOnly_AcceptsTheLiteral()
    {
        // Arrange, Act
        var tried = UserProfile.Keys.Sk.TryParse("PROFILE", out _);

        // Assert
        await Assert.That(tried).IsTrue();
    }

    [Test]
    public async Task Parse_PlaceholdersWithoutALiteralBetween_SplitsAtTheFixedWidth()
    {
        // Arrange, Act
        var parts = Adjacent.Keys.Sk.Parse("007ab");

        // Assert
        await Assert.That(parts).IsEqualTo(new Adjacent.Keys.SkParts(7, "ab"));
    }

    [Test]
    public async Task Parse_DateTime_HasKindUtc()
    {
        // Arrange, Act
        var parsed = KeyFormats.Keys.DateTimePk.Parse("T#2026-10-31T12:34:56.789Z").Utc;

        // Assert
        await Assert.That((parsed, parsed.Kind)).IsEqualTo((Utc, DateTimeKind.Utc));
    }

    [Test]
    [MethodDataSource(nameof(RejectedTexts))]
    public async Task TryParse_TextThatFormatWouldNotReproduce_Fails(KeyParseCall tryParse)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(tryParse);

        // Act
        var parsed = tryParse();

        // Assert
        await Assert.That(parsed).IsFalse();
    }

    [Test]
    public async Task TryParse_InvalidUtf8_Fails()
    {
        // Arrange
        byte[] text = [.. "TENANT#"u8, 0xFF, .. "#USER#1"u8];

        // Act
        var parsed = Member.Keys.Pk.TryParse(text, out var parts);

        // Assert
        await Assert.That((parsed, parts)).IsEqualTo((false, default(Member.Keys.PkParts)));
    }

    [Test]
    public async Task Parse_TextOfTheWidestAndLowestValues_ReproducesTheText()
    {
        // Arrange (the limits themselves pass the range checks)
        string[] keys =
        [
            KeyFormats.Keys.SbytePk.Format(sbyte.MinValue), KeyFormats.Keys.BytePk.Format(byte.MaxValue), KeyFormats.Keys.IntPk.Format(int.MaxValue), KeyFormats.Keys.LongPk.Format(long.MaxValue),
            KeyFormats.Keys.UlongPk.Format(ulong.MaxValue), KeyFormats.Keys.FixedIntPk.Format(int.MinValue), KeyFormats.Keys.DigitsBytePk.Format(byte.MaxValue), KeyFormats.Keys.TicksPk.Format(DateTimeOffset.MaxValue),
        ];

        // Act
        string[] reformatted =
        [
            KeyFormats.Keys.SbytePk.Format(KeyFormats.Keys.SbytePk.Parse(keys[0]).SByte), KeyFormats.Keys.BytePk.Format(KeyFormats.Keys.BytePk.Parse(keys[1]).Byte),
            KeyFormats.Keys.IntPk.Format(KeyFormats.Keys.IntPk.Parse(keys[2]).Int), KeyFormats.Keys.LongPk.Format(KeyFormats.Keys.LongPk.Parse(keys[3]).Long),
            KeyFormats.Keys.UlongPk.Format(KeyFormats.Keys.UlongPk.Parse(keys[4]).ULong), KeyFormats.Keys.FixedIntPk.Format(KeyFormats.Keys.FixedIntPk.Parse(keys[5]).Int),
            KeyFormats.Keys.DigitsBytePk.Format(KeyFormats.Keys.DigitsBytePk.Parse(keys[6]).Byte), KeyFormats.Keys.TicksPk.Format(KeyFormats.Keys.TicksPk.Parse(keys[7]).At),
        ];

        // Assert
        await Assert.That(reformatted).IsEquivalentTo(keys);
    }

    [Test]
    public async Task Parse_EveryFormat_ReproducesTheText()
    {
        // Arrange
        string[] keys =
        [
            KeyFormats.Keys.GuidPk.Format(OrderId), KeyFormats.Keys.NamePk.Format(Plan.Pro), KeyFormats.Keys.ValuePk.Format(Plan.Pro), KeyFormats.Keys.ByteEnumPk.Format(Tier.High),
            KeyFormats.Keys.DecimalPk.Format(-1.5m), KeyFormats.Keys.FixedDecimalPk.Format(-1.5m), KeyFormats.Keys.YearPk.Format(At), KeyFormats.Keys.MonthPk.Format(At),
            KeyFormats.Keys.DayPk.Format(At), KeyFormats.Keys.HourPk.Format(At), KeyFormats.Keys.MinutePk.Format(At), KeyFormats.Keys.SecondPk.Format(At), KeyFormats.Keys.MicroPk.Format(AtWithTicks),
            KeyFormats.Keys.DateTimeTicksPk.Format(Utc), KeyFormats.Keys.DatePk.Format(new DateOnly(2026, 2, 28)), KeyFormats.Keys.CustomPk.Format(IPAddress.Loopback),
        ];

        // Act
        string[] reformatted =
        [
            KeyFormats.Keys.GuidPk.Format(KeyFormats.Keys.GuidPk.Parse(keys[0]).Id), KeyFormats.Keys.NamePk.Format(KeyFormats.Keys.NamePk.Parse(keys[1]).Plan),
            KeyFormats.Keys.ValuePk.Format(KeyFormats.Keys.ValuePk.Parse(keys[2]).Plan), KeyFormats.Keys.ByteEnumPk.Format(KeyFormats.Keys.ByteEnumPk.Parse(keys[3]).Tier),
            KeyFormats.Keys.DecimalPk.Format(KeyFormats.Keys.DecimalPk.Parse(keys[4]).Money), KeyFormats.Keys.FixedDecimalPk.Format(KeyFormats.Keys.FixedDecimalPk.Parse(keys[5]).Money),
            KeyFormats.Keys.YearPk.Format(KeyFormats.Keys.YearPk.Parse(keys[6]).At), KeyFormats.Keys.MonthPk.Format(KeyFormats.Keys.MonthPk.Parse(keys[7]).At),
            KeyFormats.Keys.DayPk.Format(KeyFormats.Keys.DayPk.Parse(keys[8]).At), KeyFormats.Keys.HourPk.Format(KeyFormats.Keys.HourPk.Parse(keys[9]).At),
            KeyFormats.Keys.MinutePk.Format(KeyFormats.Keys.MinutePk.Parse(keys[10]).At), KeyFormats.Keys.SecondPk.Format(KeyFormats.Keys.SecondPk.Parse(keys[11]).At),
            KeyFormats.Keys.MicroPk.Format(KeyFormats.Keys.MicroPk.Parse(keys[12]).At), KeyFormats.Keys.DateTimeTicksPk.Format(KeyFormats.Keys.DateTimeTicksPk.Parse(keys[13]).Utc),
            KeyFormats.Keys.DatePk.Format(KeyFormats.Keys.DatePk.Parse(keys[14]).Date), KeyFormats.Keys.CustomPk.Format(KeyFormats.Keys.CustomPk.Parse(keys[15]).Address),
        ];

        // Assert
        await Assert.That(reformatted).IsEquivalentTo(keys);
    }

    [Test]
    public async Task Parse_NoKeyOfTheTemplate_ThrowsWithoutTheText()
    {
        // Arrange
        const string Text = "secret-value";

        // Act
        var exception = Assert.Throws<KeyFormatException>(() => Member.Keys.Pk.Parse(Text));

        // Assert
        await Assert.That(exception.Message).IsEqualTo("The text is not a key of the template TENANT#{Tenant}#USER#{Id}.");
    }

    [Test]
    public async Task Parse_NoKeyOfTheTemplateAsUtf8_Throws()
    {
        // Arrange
        byte[] text = [0xFF];

        // Act
        var act = () => Member.Keys.Pk.Parse(text);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>();
    }

    [Test]
    public async Task Of_CompositeKey_WritesPartitionAndSortKey()
    {
        // Arrange, Act
        var json = KeyJson(KeyedOrder.Keys.Of(User, At, OrderId));

        // Assert
        await Assert.That(json).IsEqualTo($$$"""{"pk":{"S":"USER#{{{UserText}}}"},"sk":{"S":"{{{OrderKey}}}"}}""");
    }

    [Test]
    public async Task Of_NamedArguments_BindByPropertyName()
    {
        // Arrange, Act
        var json = KeyJson(KeyedOrder.Keys.Of(orderId: OrderId, createdAt: At, userId: User));

        // Assert
        await Assert.That(json).IsEqualTo($$$"""{"pk":{"S":"USER#{{{UserText}}}"},"sk":{"S":"{{{OrderKey}}}"}}""");
    }

    [Test]
    public async Task Of_LiteralsOnly_TakesNoParameters()
    {
        // Arrange, Act
        var json = KeyJson(SiteSettings.Keys.Of());

        // Assert
        await Assert.That(json).IsEqualTo("""{"pk":{"S":"CONFIG"},"sk":{"S":"GLOBAL"}}""");
    }

    [Test]
    public async Task Of_LiteralSortKey_TakesOnlyThePartitionValues()
    {
        // Arrange, Act
        var json = KeyJson(UserProfile.Keys.Of("alice"));

        // Assert
        await Assert.That(json).IsEqualTo("""{"pk":{"S":"USER#alice"},"sk":{"S":"PROFILE"}}""");
    }

    [Test]
    public async Task Of_PlainKeys_WritesTheAttributesOfThePropertiesThroughTheirConverters()
    {
        // Arrange, Act
        var json = KeyJson(Device.Keys.Of("a1", 3));

        // Assert
        await Assert.That(json).IsEqualTo("""{"Serial":{"S":"a1"},"Slot":{"N":"3"}}""");
    }

    [Test]
    public async Task Of_PlainEnumKey_WritesItsName()
    {
        // Arrange, Act
        var json = KeyJson(Quota.Keys.Of(Plan.Pro));

        // Assert
        await Assert.That(json).IsEqualTo("""{"Plan":{"S":"Pro"}}""");
    }

    [Test]
    public async Task Of_PropertyInBothKeys_TakesItOnce()
    {
        // Arrange, Act
        var json = KeyJson(Account.Keys.Of("a", 7));

        // Assert
        await Assert.That(json).IsEqualTo("""{"Id":{"S":"a"},"sk":{"S":"ACCOUNT#a#V007"}}""");
    }

    [Test]
    public async Task Of_PropertyInBothKeysWithOtherFormats_WritesEachFormat()
    {
        // Arrange, Act
        var json = KeyJson(Ledger.Keys.Of(At, OrderId));

        // Assert
        await Assert.That(json).IsEqualTo($$$"""{"pk":{"S":"LEDGER#2026-10"},"sk":{"S":"AT#2026-10-31T12:34:56.7890000Z#{{{OrderText}}}"}}""");
    }

    [Test]
    public async Task Of_InvalidValue_FailsWhenTheKeyIsWritten()
    {
        // Arrange
        var key = UserProfile.Keys.Of("a#b");

        // Act
        var act = () => KeyJson(key);

        // Assert
        await Assert.That(act).ThrowsExactly<KeyFormatException>();
    }

    private static IEnumerable<(string? Name, Type Type)> Parameters(Type builder, string method) =>
        builder.GetMethod(method)!.GetParameters().Select(static parameter => (parameter.Name, parameter.ParameterType));

    private static string KeyJson<TKey>(TKey key)
        where TKey : IKey => TestJson.Write(writer =>
        {
            writer.WriteStartItem();
            key.WriteKey(writer);
            writer.WriteEndItem();
        });
}
