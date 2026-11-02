using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Mapping.KeyEntities;

/// <summary>
/// The ranges of a key builder hold what they promise when the rows they select are the rows the values say. Each test renders the conditions into a request, compares real
/// keys with what went out by their UTF-8 bytes, which is what DynamoDB does, and takes the expectation from the values alone: the order of decimal, of the instants, of the
/// tuple. Neither side looks at how a key is encoded.
/// </summary>
public sealed partial class GeneratedSortKeyConditionTests
{
    private static readonly string[] RangeNames = ["Between", "Ge", "Gt", "Le", "Lt"];

    private static readonly Guid First = new("00000000-0000-0000-0000-000000000000");

    private static readonly Guid Last = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Ranges_TimestampBeforeADelimiter_FollowTheInstants()
    {
        // Arrange
        DateTimeOffset[] instants =
        [
            new(1999, 12, 31, 23, 59, 59, 999, TimeSpan.Zero), new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), Noon, Noon.AddMilliseconds(1), Noon.AddSeconds(1), new(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(5)),
        ];

        // Act
        var wrong = WrongRanges(
            instants,
            Comparer<DateTimeOffset>.Default.Compare,
            static at => [KeyedOrder.Keys.Sk.Format(at, First), KeyedOrder.Keys.Sk.Format(at, Last)],
            KeyedOrder.Keys.Sk.Ge,
            KeyedOrder.Keys.Sk.Gt,
            KeyedOrder.Keys.Sk.Le,
            KeyedOrder.Keys.Sk.Lt,
            KeyedOrder.Keys.Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_FixedPointAfterALiteral_FollowTheNumbers()
    {
        // Arrange (the sign letter and the nine's complement of a negative number are what is at stake)
        decimal[] totals = [-99_999_999.99m, -1000.5m, -12.5m, -12.49m, -0.01m, 0m, 0.01m, 12.49m, 12.5m, 100m, 99_999_999.99m];

        // Act
        var wrong = WrongRanges(
            totals,
            decimal.Compare,
            static total => [KeyedOrder.Keys.Lsi1Sk.Format(total)],
            KeyedOrder.Keys.Lsi1Sk.Ge,
            KeyedOrder.Keys.Lsi1Sk.Gt,
            KeyedOrder.Keys.Lsi1Sk.Le,
            KeyedOrder.Keys.Lsi1Sk.Lt,
            KeyedOrder.Keys.Lsi1Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_FixedPointAlone_FollowTheNumbers()
    {
        // Arrange
        decimal[] totals = [-12.5m, -12.49m, 0m, 12.49m, 12.5m];

        // Act
        var wrong = WrongRanges(
            totals,
            decimal.Compare,
            static total => [KeyedOrder.Keys.Gsi1Sk.Format(total)],
            KeyedOrder.Keys.Gsi1Sk.Ge,
            KeyedOrder.Keys.Gsi1Sk.Gt,
            KeyedOrder.Keys.Gsi1Sk.Le,
            KeyedOrder.Keys.Gsi1Sk.Lt,
            KeyedOrder.Keys.Gsi1Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_DigitsAtTheEnd_FollowTheNumbers()
    {
        // Arrange (9 and 10 are where plain digits would go wrong)
        int[] slots = [0, 1, 9, 10, 99, 100, 999];

        // Act
        var wrong = WrongRanges(
            slots,
            static (left, right) => left.CompareTo(right),
            static slot => [Handles.Keys.Sk.Format(slot)],
            Handles.Keys.Sk.Ge,
            Handles.Keys.Sk.Gt,
            Handles.Keys.Sk.Le,
            Handles.Keys.Sk.Lt,
            Handles.Keys.Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_DateAsTheFirstOfThreeFields_FollowTheDays()
    {
        // Arrange
        DateOnly[] days = [new(1999, 12, 31), new(2000, 1, 1), new(2026, 2, 28), new(2026, 3, 1), new(2026, 12, 31)];

        // Act
        var wrong = WrongRanges(
            days,
            static (left, right) => left.CompareTo(right),
            static day => [LogLine.Keys.Sk.Format(day, Noon, 0), LogLine.Keys.Sk.Format(day, Noon.AddDays(1), 9999)],
            LogLine.Keys.Sk.Ge,
            LogLine.Keys.Sk.Gt,
            LogLine.Keys.Sk.Le,
            LogLine.Keys.Sk.Lt,
            LogLine.Keys.Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task OpenRanges_TwoLeadingFields_FollowTheTuple()
    {
        // Arrange (an open range goes on past the first field, which is what the order of the tuple means)
        (DateOnly Day, DateTimeOffset At)[] positions =
        [
            (new(2026, 3, 1), Noon), (new(2026, 3, 1), Noon.AddMilliseconds(1)), (new(2026, 3, 2), Noon.AddHours(-1)), (new(2026, 3, 2), Noon),
        ];

        // Act
        var wrong = WrongRanges(
            positions,
            static (left, right) => left.CompareTo(right),
            static position => [LogLine.Keys.Sk.Format(position.Day, position.At, 0), LogLine.Keys.Sk.Format(position.Day, position.At, 9999)],
            static position => LogLine.Keys.Sk.Ge(position.Day, position.At),
            static position => LogLine.Keys.Sk.Gt(position.Day, position.At),
            static position => LogLine.Keys.Sk.Le(position.Day, position.At),
            static position => LogLine.Keys.Sk.Lt(position.Day, position.At),
            between: null);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Between_LastFieldUnderALeadingField_StaysUnderThatField()
    {
        // Arrange
        DateTimeOffset[] instants = [Noon, Noon.AddMilliseconds(1), Noon.AddSeconds(1)];

        // Act
        var wrong = WrongRanges(
            instants,
            Comparer<DateTimeOffset>.Default.Compare,
            static at => [LogLine.Keys.Sk.Format(DateOnly.FromDateTime(Noon.UtcDateTime), at, 0), LogLine.Keys.Sk.Format(DateOnly.FromDateTime(Noon.UtcDateTime), at, 9999)],
            ge: null,
            gt: null,
            le: null,
            lt: null,
            static (from, to) => LogLine.Keys.Sk.Between(DateOnly.FromDateTime(Noon.UtcDateTime), from, to));

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_GuidAtTheEnd_FollowItsText()
    {
        // Arrange
        Guid[] ids = [First, new("0190a5f0-0000-7000-8000-000000000000"), new("0190a5f0-0001-7000-8000-000000000000"), Last];

        // Act
        var wrong = WrongRanges(
            ids,
            static (left, right) => string.CompareOrdinal(left.ToString("D"), right.ToString("D")),
            static id => [Ledger.Keys.Sk.Format(Noon, id)],
            static id => Ledger.Keys.Sk.Ge(Noon, id),
            static id => Ledger.Keys.Sk.Gt(Noon, id),
            static id => Ledger.Keys.Sk.Le(Noon, id),
            static id => Ledger.Keys.Sk.Lt(Noon, id),
            static (from, to) => Ledger.Keys.Sk.Between(Noon, from, to));

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_TruncatedTimestamp_FollowTheBuckets()
    {
        // Arrange (a bucket is one key, so what is in the same minute counts as equal)
        DateTimeOffset[] instants = [Noon, Noon.AddSeconds(30), Noon.AddSeconds(59), Noon.AddMinutes(1), Noon.AddHours(1)];

        // Act
        var wrong = WrongRanges(
            instants,
            static (left, right) => string.CompareOrdinal(Minute(left), Minute(right)),
            static at => [SameSlot.Keys.Sk.Format(at)],
            SameSlot.Keys.Sk.Ge,
            SameSlot.Keys.Sk.Gt,
            SameSlot.Keys.Sk.Le,
            SameSlot.Keys.Sk.Lt,
            SameSlot.Keys.Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_StringAtTheEnd_FollowTheUtf8Bytes()
    {
        // Arrange (a value that continues another one sorts above it and is no part of it, and U+FF5E sorts below an emoji in UTF-8 and above it in UTF-16)
        string[] targets = ["a", "a b", "a!", "aa", "ab", "b", "ba", "é", "～", "😀"];

        // Act
        var wrong = WrongRanges(
            targets,
            CompareUtf8,
            static target => [Edge.Keys.Sk.Format(target)],
            Edge.Keys.Sk.Ge,
            Edge.Keys.Sk.Gt,
            Edge.Keys.Sk.Le,
            Edge.Keys.Sk.Lt,
            Edge.Keys.Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_FixedWidthBeforeAStringAtTheEnd_FollowTheTuple()
    {
        // Arrange
        (int Code, string Name)[] positions = [(7, "a"), (7, "ab"), (7, "b"), (8, "a")];

        // Act
        var wrong = WrongRanges(
            positions,
            static (left, right) => left.Code != right.Code ? left.Code.CompareTo(right.Code) : CompareUtf8(left.Name, right.Name),
            static position => [Adjacent.Keys.Sk.Format(position.Code, position.Name)],
            static position => Adjacent.Keys.Sk.Ge(position.Code, position.Name),
            static position => Adjacent.Keys.Sk.Gt(position.Code, position.Name),
            static position => Adjacent.Keys.Sk.Le(position.Code, position.Name),
            static position => Adjacent.Keys.Sk.Lt(position.Code, position.Name),
            between: null);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Ranges_StringBeforeASpace_FollowTheUtf8Bytes()
    {
        // Arrange (a space is the one delimiter below every character a value may hold, so a longer value sorts above a shorter one)
        string[] names = ["a", "a!", "a#", "aa", "b"];

        // Act
        var wrong = WrongRanges(
            names,
            CompareUtf8,
            static name => [Ranged.Keys.Sk.Format(name, 0), Ranged.Keys.Sk.Format(name, 99)],
            Ranged.Keys.Sk.Ge,
            Ranged.Keys.Sk.Gt,
            Ranged.Keys.Sk.Le,
            Ranged.Keys.Sk.Lt,
            Ranged.Keys.Sk.Between);

        // Assert
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task HandBuiltRanges_StringBeforeADelimiter_GetRowsWrong()
    {
        // Arrange (what the builder leaves out, written by hand: A B sorts below A although it is the larger value)
        string[] columns = ["A", "A B", "AB"];

        // Act
        var wrong = WrongRanges(
            columns,
            CompareUtf8,
            static column => [GridCell.Keys.Sk.Format(column, 1)],
            static column => SortKeyCondition.Ge(GridCell.Keys.Sk.Prefix(column)),
            static column => SortKeyCondition.Gt(GridCell.Keys.Sk.UpperBound(column)),
            static column => SortKeyCondition.Le(GridCell.Keys.Sk.UpperBound(column)),
            static column => SortKeyCondition.Lt(GridCell.Keys.Sk.Prefix(column)),
            static (from, to) => SortKeyCondition.Between(GridCell.Keys.Sk.Prefix(from), GridCell.Keys.Sk.UpperBound(to)));

        // Assert
        await Assert.That(wrong).Contains("ge A: CELL#A B#001 should match");
    }

    [Test]
    public async Task HandBuiltRange_UpperBoundOfAStringThatEndsTheKey_HidesLongerValues()
    {
        // Arrange (a bound of that kind is why such a key compares with the key itself)
        string[] targets = ["a", "ab"];

        // Act
        var wrong = WrongRanges(
            targets,
            CompareUtf8,
            static target => [Edge.Keys.Sk.Format(target)],
            ge: null,
            static target => SortKeyCondition.Gt(Edge.Keys.Sk.Format(target) + "\U0010FFFF"),
            le: null,
            lt: null,
            between: null);

        // Assert
        await Assert.That(wrong).Contains("gt a: TO#ab should match");
    }

    [Test]
    public async Task Between_NumberUnderAStringThatCouldSortAnywhere_ReadsOnlyThatString()
    {
        // Arrange (A B sorts below A and AB above it, which is why this template has no open range, and neither is inside this one)
        string[] keys = [GridCell.Keys.Sk.Format("A", 3), GridCell.Keys.Sk.Format("A B", 3), GridCell.Keys.Sk.Format("AB", 3), GridCell.Keys.Sk.Format("A", 7)];

        // Act
        var condition = Render(GridCell.Keys.Sk.Between("A", 1, 5));

        // Assert
        await Assert.That(keys.Where(key => condition.Holds(key))).IsEquivalentTo(["CELL#A#003"]);
    }

    [Test]
    public async Task Between_StringThatEndsTheKey_TakesWholeValuesUnderTheLeadingField()
    {
        // Arrange
        string[] keys = [Member.Keys.Sk.Format("EU", "a"), Member.Keys.Sk.Format("EU", "m"), Member.Keys.Sk.Format("EU", "ma"), Member.Keys.Sk.Format("EUR", "b")];

        // Act
        var condition = Render(Member.Keys.Sk.Between("EU", "a", "m"));

        // Assert
        await Assert.That(keys.Where(key => condition.Holds(key))).IsEquivalentTo(["REGION#EU#CITY#a", "REGION#EU#CITY#m"]);
    }

    [Test]
    public async Task Between_PlaceholdersNamedLikeTheBounds_TakesEach()
    {
        // Arrange, Act
        var condition = Render(Ranged.Keys.ByBoundsSk.Between(1, 2, 3, 4));

        // Assert
        await Assert.That((condition.Holds("01#02#03"), condition.Holds("01#02#05"), condition.Holds("01#03#03"))).IsEqualTo((true, false, false));
    }

    [Test]
    public async Task Between_BothPlaceholdersNamedLikeTheBounds_TakesEach()
    {
        // Arrange, Act
        var condition = Render(Ranged.Keys.ByBoundsSk.Between(1, 3, 5));

        // Assert
        await Assert.That((condition.Holds("01#03#00"), condition.Holds("01#05#99"), condition.Holds("01#06#00"))).IsEqualTo((true, true, false));
    }

    [Test]
    public async Task Eq_EveryPlaceholder_IsTheFormattedKey()
    {
        // Arrange
        var key = KeyedOrder.Keys.Sk.Format(Noon, First);

        // Act
        var condition = Render(KeyedOrder.Keys.Sk.Eq(Noon, First));

        // Assert
        await Assert.That((condition.Operator, condition.Low)).IsEqualTo(("=", key));
    }

    [Test]
    public async Task Eq_KeyOfLiterals_IsTheKey()
    {
        // Arrange, Act
        var condition = Render(SiteSettings.Keys.Sk.Eq());

        // Assert
        await Assert.That((condition.Operator, condition.Low)).IsEqualTo(("=", "GLOBAL"));
    }

    [Test]
    public async Task Eq_KeyWhoseOrderMeansNothing_IsStillGenerated()
    {
        // Arrange, Act
        var condition = Render(Unranged.Keys.Sk.Eq(10));

        // Assert
        await Assert.That((condition.Operator, condition.Low)).IsEqualTo(("=", "COUNT#10"));
    }

    [Test]
    [Arguments(typeof(Unranged.Keys.Sk))]
    [Arguments(typeof(Unranged.Keys.ByMoneySk))]
    [Arguments(typeof(Unranged.Keys.ByPlanSk))]
    [Arguments(typeof(Unranged.Keys.ByAddressSk))]
    [Arguments(typeof(Suffixed.Keys.Sk))]
    public async Task Builders_BytesThatDoNotFollowTheValues_HaveNoRange(Type builder)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(builder);

        // Act
        var names = Names(builder);

        // Assert
        await Assert.That(names.Intersect(RangeNames)).IsEmpty();
    }

    [Test]
    public async Task Builders_StringBeforeADelimiter_HaveBetweenOnTheFieldAfterIt()
    {
        // Arrange, Act (a # after a value lets A B sort below A, so an open range would miss rows, and a range of names as well)
        var arities = typeof(GridCell.Keys.Sk).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static method => RangeNames.Contains(method.Name))
            .Select(static method => $"{method.Name}/{method.GetParameters().Length}");

        // Assert
        await Assert.That(arities).IsEquivalentTo(["Between/3"]);
    }

    [Test]
    [Arguments(typeof(KeyedOrder.Keys.Sk))]
    [Arguments(typeof(Member.Keys.Sk))]
    [Arguments(typeof(Suffixed.Keys.Sk))]
    public async Task Builders_NoValueToCompareWith_HaveNoRange(Type builder)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(builder);

        // Act (a range of a prefix alone would read what lies outside the template)
        var empty = builder.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(static method => RangeNames.Contains(method.Name) && method.GetParameters().Length == 0);

        // Assert
        await Assert.That(empty).IsEmpty();
    }

    [Test]
    public async Task Ge_EmptyString_ThrowsLikeTheKeyWould()
    {
        // Arrange, Act
        var failure = await TestClient.CaptureAsync<KeyFormatException>(static () => Task.FromResult(Edge.Keys.Sk.Ge(string.Empty)));

        // Assert
        await Assert.That(failure.Message).Contains("Target");
    }

    [Test]
    public async Task Between_DelimiterInAValue_ThrowsAndDoesNotQuoteIt()
    {
        // Arrange, Act
        var failure = await TestClient.CaptureAsync<KeyFormatException>(static () => Task.FromResult(Member.Keys.Sk.Between("EU", "a#b", "c")));

        // Assert
        await Assert.That(failure.Message).DoesNotContain("a#b");
    }

    [Test]
    public async Task Le_ValueFinerThanTheFormat_Throws()
    {
        // Arrange, Act
        var failure = await TestClient.CaptureAsync<KeyFormatException>(static () => Task.FromResult(KeyedOrder.Keys.Lsi1Sk.Le(1.005m)));

        // Assert
        await Assert.That(failure.Message).Contains("Total");
    }

    [Test]
    public async Task Conditions_PlainNumber_CompareTheNumber()
    {
        // Arrange
        SortKeyCondition[] conditions = [Device.Keys.Sk.Eq(5), Device.Keys.Sk.Ge(5), Device.Keys.Sk.Gt(5), Device.Keys.Sk.Le(5), Device.Keys.Sk.Lt(5), Device.Keys.Sk.Between(5, 7)];

        // Act
        var rendered = conditions.Select(static condition => RenderRaw(condition));

        // Assert
        await Assert.That(rendered).IsEquivalentTo(
        [
            """= {"N":"5"}""", """>= {"N":"5"}""", """> {"N":"5"}""", """<= {"N":"5"}""", """< {"N":"5"}""", """BETWEEN {"N":"5"} {"N":"7"}""",
        ]);
    }

    [Test]
    public async Task Conditions_PlainString_ComparesAndMatchesAPrefix()
    {
        // Arrange
        SortKeyCondition[] conditions = [PlainKeys.Keys.Sk.Ge("a"), PlainKeys.Keys.Sk.Between("a", "b"), PlainKeys.Keys.Sk.BeginsWith("a")];

        // Act
        var rendered = conditions.Select(static condition => RenderRaw(condition));

        // Assert
        await Assert.That(rendered).IsEquivalentTo([""">= {"S":"a"}""", """BETWEEN {"S":"a"} {"S":"b"}""", """begins_with {"S":"a"}"""]);
    }

    [Test]
    public async Task Conditions_PlainInstant_ComparesTheStoredText()
    {
        // Arrange, Act
        var rendered = RenderRaw(BinaryKeyed.Keys.Sk.Lt(Noon));

        // Assert
        await Assert.That(rendered).IsEqualTo("""< {"S":"2026-10-31T12:00:00.0000000Z"}""");
    }

    [Test]
    public async Task Conditions_PlainBinary_CompareTheBytes()
    {
        // Arrange, Act
        var rendered = RenderRaw(PlainRanges.Keys.ByDigestSk.Between([1], [2]));

        // Assert
        await Assert.That(rendered).IsEqualTo("""BETWEEN {"B":"AQ=="} {"B":"Ag=="}""");
    }

    [Test]
    public async Task Conditions_PlainNullableIndexKey_TakeTheValue()
    {
        // Arrange, Act
        var rendered = RenderRaw(OwnedThing.Keys.ByOwnerSk.Ge(3));

        // Assert
        await Assert.That(rendered).IsEqualTo(""">= {"N":"3"}""");
    }

    [Test]
    [Arguments(typeof(Device.Keys.Sk), new[] { "Between", "Eq", "Ge", "Gt", "Le", "Lt" })]
    [Arguments(typeof(PlainKeys.Keys.Sk), new[] { "BeginsWith", "Between", "Eq", "Ge", "Gt", "Le", "Lt" })]
    [Arguments(typeof(PlainRanges.Keys.Sk), new[] { "Between", "Eq", "Ge", "Gt", "Le", "Lt" })]
    [Arguments(typeof(PlainRanges.Keys.ByDigestSk), new[] { "Between", "Eq", "Ge", "Gt", "Le", "Lt" })]
    [Arguments(typeof(PlainRanges.Keys.ByWaitSk), new[] { "Eq" })]
    [Arguments(typeof(PlainRanges.Keys.ByPlanSk), new[] { "Eq" })]
    [Arguments(typeof(PlainRanges.Keys.ByNoteSk), new[] { "Eq" })]
    public async Task Builders_PlainSortKey_OfferWhatItsStoredFormFits(Type builder, string[] expected)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(builder);

        // Act
        var names = Names(builder).Intersect([.. RangeNames, "BeginsWith", "Eq"]);

        // Assert
        await Assert.That(names).IsEquivalentTo(expected);
    }

    private static IEnumerable<string> Names(Type builder) => builder.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(static method => method.Name).Distinct();

    private static int CompareUtf8(string left, string right) => Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right));

    private static string Minute(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    // Every condition with every value as its bound, against every key of every value. A row of the result is one key on the wrong side of one condition.
    private static List<string> WrongRanges<T>(
        T[] values,
        Comparison<T> order,
        Func<T, string[]> keys,
        Func<T, SortKeyCondition>? ge,
        Func<T, SortKeyCondition>? gt,
        Func<T, SortKeyCondition>? le,
        Func<T, SortKeyCondition>? lt,
        Func<T, T, SortKeyCondition>? between)
    {
        var wrong = new List<string>();
        foreach (var bound in values)
        {
            Check(ge, static difference => difference >= 0, nameof(ge));
            Check(gt, static difference => difference > 0, nameof(gt));
            Check(le, static difference => difference <= 0, nameof(le));
            Check(lt, static difference => difference < 0, nameof(lt));
            foreach (var upper in values.Where(upper => between is not null && order(bound, upper) <= 0))
            {
                var condition = Render(between!(bound, upper));
                foreach (var value in values)
                {
                    Note(condition, $"between {bound} and {upper}", value, order(value, bound) >= 0 && order(value, upper) <= 0);
                }
            }

            void Check(Func<T, SortKeyCondition>? make, Func<int, bool> expected, string name)
            {
                if (make is not null)
                {
                    var condition = Render(make(bound));
                    foreach (var value in values)
                    {
                        Note(condition, $"{name} {bound}", value, expected(order(value, bound)));
                    }
                }
            }
        }

        return wrong;

        void Note(Rendered condition, string name, T value, bool expected)
        {
            foreach (var key in keys(value))
            {
                if (condition.Holds(key) != expected)
                {
                    wrong.Add($"{name}: {key} should {(expected ? string.Empty : "not ")}match");
                }
            }
        }
    }

    private static Rendered Render(SortKeyCondition condition)
    {
        var (_, operation, low, high) = ReadKeyCondition(condition);
        return new Rendered(operation, low.GetProperty("S").GetString()!, high?.GetProperty("S").GetString());
    }

    private static string RenderRaw(SortKeyCondition condition)
    {
        var (_, operation, low, high) = ReadKeyCondition(condition);
        return high is { } upper ? $"{operation} {low.GetRawText()} {upper.GetRawText()}" : $"{operation} {low.GetRawText()}";
    }

    private static (string Name, string Operator, JsonElement Low, JsonElement? High) ReadKeyCondition(SortKeyCondition condition)
    {
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        using var body = JsonDocument.Parse(client.Table("Shop", "pk", "sk").Query("p", condition).ToRequestJson());
        var values = body.RootElement.GetProperty("ExpressionAttributeValues");
        var match = KeyCondition().Match(body.RootElement.GetProperty("KeyConditionExpression").GetString()!);
        var low = values.GetProperty(match.Groups["low"].Value).Clone();
        var high = match.Groups["high"].Success ? values.GetProperty(match.Groups["high"].Value).Clone() : (JsonElement?)null;
        return (match.Groups["name"].Value, match.Groups["function"].Success ? "begins_with" : match.Groups["operator"].Value, low, high);
    }

    // #n0 = :v0 AND #n1 BETWEEN :v1 AND :v2, or #n0 = :v0 AND begins_with(#n1, :v1): what follows the partition key.
    [GeneratedRegex(@" AND (?:(?<function>begins_with)\((?<name>#\w+), (?<low>:\w+)\)|(?<name>#\w+) (?<operator>\S+) (?<low>:\w+)(?: AND (?<high>:\w+))?)$")]
    private static partial Regex KeyCondition();

    /// <summary>What a condition compares with, and the comparison as DynamoDB does it: by the bytes of the UTF-8 form.</summary>
    /// <param name="Operator">The operator.</param>
    /// <param name="Low">The value, or the lower bound.</param>
    /// <param name="High">The upper bound of a range.</param>
    private readonly record struct Rendered(string Operator, string Low, string? High)
    {
        public bool Holds(string key) => Operator switch
        {
            "=" => CompareUtf8(key, Low) == 0,
            ">=" => CompareUtf8(key, Low) >= 0,
            ">" => CompareUtf8(key, Low) > 0,
            "<=" => CompareUtf8(key, Low) <= 0,
            "<" => CompareUtf8(key, Low) < 0,
            "BETWEEN" => CompareUtf8(key, Low) >= 0 && CompareUtf8(key, High!) <= 0,
            _ => throw new InvalidOperationException(Operator),
        };
    }
}
