using System.Globalization;
using System.Text;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Serialization;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class OperationJsonTests
{
    // The wrappers, each with the zero member, which writes nothing, and a member that is written.
    public static IEnumerable<(Action<Utf8JsonWriter> Write, string Json)> ReturnWriters()
    {
        yield return (json => OperationJson.WriteReturnValues(json, ReturnValue.None), "{}");
        yield return (json => OperationJson.WriteReturnValues(json, ReturnValue.UpdatedNew), """{"ReturnValues":"UPDATED_NEW"}""");
        yield return (json => OperationJson.WriteReturnValuesOnConditionCheckFailure(json, ReturnValuesOnConditionCheckFailure.None), "{}");
        yield return (json => OperationJson.WriteReturnConsumedCapacity(json, ReturnConsumedCapacity.None), "{}");
        yield return (json => OperationJson.WriteReturnItemCollectionMetrics(json, ReturnItemCollectionMetrics.None), "{}");
        yield return (json => OperationJson.WriteSelect(json, SelectKind.Unspecified), "{}");
        yield return (json => OperationJson.WriteSelect(json, SelectKind.Count), """{"Select":"COUNT"}""");
    }

    [Test]
    [MethodDataSource(nameof(ReturnWriters))]
    public async Task WriteReturn_Member_WritesTheWireNameOrNothingForUnspecified(Action<Utf8JsonWriter> write, string expected)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(write);
        using var buffer = new PooledByteBuffer();

        // Act
        using (var json = WireJson.CreateWriter(buffer))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("-62135596800", 1, 1, 1, 0, 0, 0)]
    [Arguments("253402300799", 9999, 12, 31, 23, 59, 59)]
    public async Task ReadEpochSeconds_FirstAndLastSecondOfTheRange_ReturnsThatTime(string json, int year, int month, int day, int hour, int minute, int second)
    {
        // Arrange (DateTimeOffset spans 0001-01-01T00:00:00Z to 9999-12-31T23:59:59Z, whole seconds)
        var expected = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);

        // Act
        var time = TestJson.Read(json, static (ref reader) => OperationJson.ReadEpochSeconds(ref reader));

        // Assert
        await Assert.That(time).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadConsumedCapacity_NumberBeyondADouble_ThrowsJsonException()
    {
        // Arrange (telemetry writes the units back as JSON, which has no infinity)
        const string json = """{"CapacityUnits":1e999}""";

        // Act
        var act = () => TestJson.Read(json, static (ref reader) => OperationJson.ReadConsumedCapacity(ref reader));

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>();
    }

    [Test]
    public async Task ReadItems_Items_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"pk":{"S":"a"}},{"pk":{"S":"b"}}]""", pool);

        // Assert
        await Assert.That((items.Count, pool.Returned, pool.Outstanding)).IsEqualTo((2, 1, 0));
    }

    [Test]
    public async Task ReadItems_NoItems_ReturnsAnEmptyListAndTheRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("[]", pool);

        // Assert
        await Assert.That((items.Count, pool.Returned, pool.Outstanding)).IsEqualTo((0, 1, 0));
    }

    [Test]
    public async Task ReadItems_MoreItemsThanTheInitialCapacity_GrowsByDoublingAndReturnsEveryArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();
        var json = "[" + string.Join(',', Enumerable.Repeat("""{"pk":{"S":"a"}}""", 40)) + "]";

        // Act
        var items = ReadItems(json, pool);

        // Assert
        await Assert.That((items.Count, string.Join(',', pool.RentedLengths), pool.Returned, pool.Outstanding)).IsEqualTo((40, "16,32,64", 3, 0));
    }

    [Test]
    public async Task ReadItems_MalformedItemAfterGoodOnes_StillReturnsTheRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        try
        {
            ReadItems("""[{"pk":{"S":"a"}},{"pk":{"X":"b"}}]""", pool);
        }
        catch (JsonException)
        {
            // Expected: the second item has an unknown type descriptor.
        }

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task ReadItems_Items_LeavesNoReferencesInThePooledArray()
    {
        // Arrange (a pooled array that kept the dictionaries would keep the whole page alive)
        var pool = new CountingArrayPool<Item>();

        // Act
        ReadItems("""[{"pk":{"S":"a"}}]""", pool);

        // Assert
        await Assert.That(pool.ReturnedUncleared).IsFalse();
    }

    [Test]
    public async Task ReadItems_ItemsWithTheSameNames_ShareTheNameStrings()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"pk":{"S":"a"},"n":{"N":"1"}},{"pk":{"S":"b"},"n":{"N":"2"}},{"pk":{"S":"c"},"n":{"N":"3"}}]""", pool);

        // Assert
        var names = items.Select(item => item.Keys.ToArray()).ToArray();
        await Assert.That((ReferenceEquals(names[0][0], names[1][0]), ReferenceEquals(names[0][1], names[2][1]), pool.Outstanding)).IsEqualTo((true, true, 0));
    }

    [Test]
    public async Task ReadItems_NamesInAnotherOrderPerItem_ReadsEveryValueUnderItsName()
    {
        // Arrange (optional attributes are missing from some items, which moves the others)
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"a":{"S":"1"},"b":{"S":"2"},"c":{"S":"3"}},{"c":{"S":"6"},"a":{"S":"4"}},{"b":{"S":"5"}}]""", pool);

        // Assert
        await Assert.That(string.Join(',', items.Select(item => string.Join('+', item.OrderBy(entry => entry.Key).Select(entry => entry.Key + entry.Value.AsString())))))
            .IsEqualTo("a1+b2+c3,a4+c6,b5");
    }

    [Test]
    public async Task ReadItems_NamesInMapsAndLists_ShareTheNameStringsWithTheTopLevel()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"a":{"S":"x"},"m":{"M":{"a":{"S":"y"}}}},{"l":{"L":[{"M":{"a":{"S":"z"}}}]}}]""", pool);

        // Assert
        var top = items[0].Keys.First();
        var inMap = items[0]["m"].AsMap().Keys.First();
        var inList = items[1]["l"].AsList()[0].AsMap().Keys.First();
        await Assert.That((ReferenceEquals(top, inMap), ReferenceEquals(top, inList))).IsEqualTo((true, true));
    }

    [Test]
    public async Task ReadItems_EscapedNames_ReadsEachNameUnescaped()
    {
        // Arrange (ab is "ab"; a\\b is a, a backslash and b; a\b is a and a backspace)
        var pool = new CountingArrayPool<Item>();
        const string json = """[{"a@@U0062@@":{"N":"1"}},{"ab":{"N":"2"}},{"a\\b":{"N":"3"}},{"a\b":{"N":"4"}},{"a\\b":{"N":"5"}}]""";

        // Act
        var items = ReadItems(json.Replace("@@U0062@@", "\\u0062", StringComparison.Ordinal), pool);

        // Assert
        await Assert.That(string.Join(',', items.Select(item => item.Single().Key + item.Single().Value.AsNumber())))
            .IsEqualTo("ab1,ab2,a\\b3,a\b4,a\\b5");
    }

    [Test]
    public async Task ReadItems_EscapedSpellingOfAnAttributeNameInTheSameItem_ThrowsJsonException()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();
        var json = """[{"ab":{"N":"1"},"a@@U0062@@":{"N":"2"}}]""".Replace("@@U0062@@", "\\u0062", StringComparison.Ordinal);

        // Act
        var act = () => ReadItems(json, pool);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task ReadItems_NonAsciiNames_ReadsEachName()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"Größe":{"N":"1"},"名前":{"N":"2"}},{"Größe":{"N":"3"},"名前":{"N":"4"}}]""", pool);

        // Assert
        await Assert.That(string.Join(',', items.Select(item => string.Join('+', item.Select(entry => entry.Key + entry.Value.AsNumber())))))
            .IsEqualTo("Größe1+名前2,Größe3+名前4");
    }

    [Test]
    public async Task ReadItems_MoreDistinctNamesThanTheTableHolds_ReadsEveryValueUnderItsName()
    {
        // Arrange (200 names per item overflow the table of the page; the second item lists them in reverse)
        var pool = new CountingArrayPool<Item>();
        var forward = Enumerable.Range(0, 200).Select(i => $"\"attribute{i}\":{{\"N\":\"{i}\"}}");
        var json = "[{" + string.Join(',', forward) + "},{" + string.Join(',', forward.Reverse()) + "}]";

        // Act
        var items = ReadItems(json, pool);

        // Assert
        await Assert.That(items.All(item => item.Count == 200 && Enumerable.Range(0, 200).All(i => item[$"attribute{i}"].AsNumber() == i.ToString(CultureInfo.InvariantCulture)))).IsTrue();
    }

    [Test]
    public async Task ReadItems_TruncatedItemAfterCachedNames_ThrowsJsonExceptionAndReturnsTheRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        JsonException? error = null;
        try
        {
            ReadItems("""[{"pk":{"S":"a"},"sk":{"S":"b"}},{"pk":{"S":"c"},"sk":""", pool);
        }
        catch (JsonException exception)
        {
            error = exception;
        }

        // Assert
        await Assert.That((error is not null, pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task ReadItems_ItemWithABadValueAfterCachedNames_ThrowsJsonException()
    {
        // Arrange (the name of the bad attribute is in the table already)
        var pool = new CountingArrayPool<Item>();

        // Act
        var act = () => ReadItems("""[{"pk":{"S":"a"}},{"pk":5}]""", pool);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task ReadItems_ItemsOfOneShape_ShareTheShapeAndHoldTheirOwnValues()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"a":{"N":"1"}},{"a":{"N":"2"}}]""", pool);

        // Assert
        await Assert.That((ReferenceEquals(((Item)items[0]).Shape, ((Item)items[1]).Shape), items[0]["a"].AsNumber(), items[1]["a"].AsNumber())).IsEqualTo((true, "1", "2"));
    }

    [Test]
    public async Task ReadItems_WritingIntoOneItem_LeavesTheOtherItemsOfThePageAlone()
    {
        // Arrange
        var items = ReadItems("""[{"a":{"N":"1"}},{"a":{"N":"2"}}]""", new CountingArrayPool<Item>());
        var written = (Item)items[0];

        // Act
        written["a"] = 5;
        written["b"] = 6;

        // Assert
        await Assert.That((items[1].Count, items[1]["a"].AsNumber(), written.Count)).IsEqualTo((1, "2", 2));
    }

    [Test]
    public async Task ReadItems_ItemWithFewerAttributesThanTheOneBefore_HasOnlyItsOwn()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"a":{"N":"1"},"b":{"N":"2"}},{"a":{"N":"3"}}]""", pool);

        // Assert
        await Assert.That((items[1].Count, items[1]["a"].AsNumber(), items[1].ContainsKey("b"))).IsEqualTo((1, "3", false));
    }

    [Test]
    public async Task ReadItems_ItemWithMoreAttributesThanTheOneBefore_HasThemAll()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"a":{"N":"1"}},{"a":{"N":"2"},"b":{"N":"3"}}]""", pool);

        // Assert
        await Assert.That((items[1].Count, items[1]["a"].AsNumber(), items[1]["b"].AsNumber())).IsEqualTo((2, "2", "3"));
    }

    [Test]
    public async Task ReadItems_ItemThatMovesAwayFromTheShapeAfterItsFirstName_ReadsEveryValueUnderItsName()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"a":{"N":"1"},"b":{"N":"2"}},{"a":{"N":"3"},"c":{"N":"4"}}]""", pool);

        // Assert
        await Assert.That((items[1]["a"].AsNumber(), items[1]["c"].AsNumber(), items[1].ContainsKey("b"))).IsEqualTo(("3", "4", false));
    }

    [Test]
    public async Task ReadItems_DuplicateNameAfterAKnownShape_ThrowsJsonException()
    {
        // Arrange (the first name follows the shape, the second does not, and both items have two attributes)
        var pool = new CountingArrayPool<Item>();

        // Act
        var act = () => ReadItems("""[{"a":{"N":"1"},"b":{"N":"2"}},{"a":{"N":"3"},"a":{"N":"4"}}]""", pool);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("The attribute name \"a\" occurs more than once.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadItems_EscapedSpellingOfTheNameOfThePreviousItem_SharesTheShape()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();
        var json = """[{"ab":{"N":"1"}},{"a@@U0062@@":{"N":"2"}}]""".Replace("@@U", "\\u", StringComparison.Ordinal).Replace("@@", string.Empty, StringComparison.Ordinal);

        // Act
        var items = ReadItems(json, pool);

        // Assert
        await Assert.That((ReferenceEquals(((Item)items[0]).Shape, ((Item)items[1]).Shape), items[1]["ab"].AsNumber())).IsEqualTo((true, "2"));
    }

    [Test]
    public async Task ReadItems_NonAsciiNamesInEveryItem_SharesTheShape()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();

        // Act
        var items = ReadItems("""[{"Größe":{"N":"1"}},{"Größe":{"N":"2"}}]""", pool);

        // Assert
        await Assert.That(ReferenceEquals(((Item)items[0]).Shape, ((Item)items[1]).Shape)).IsTrue();
    }

    [Test]
    public async Task ReadItems_ShuffledItemsOfAPageWithPools_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();
        var pools = TestPools.Create();

        // Act
        var items = TestJson.Read(
            """[{"a":{"N":"1"},"b":{"N":"2"}},{"b":{"N":"3"},"a":{"N":"4"}}]""",
            (ref reader) => OperationJson.ReadItems(ref reader, pool, pools.Pools));

        // Assert
        await Assert.That((items[1]["a"].AsNumber(), pool.Outstanding, pools.Outstanding)).IsEqualTo(("4", 0, 0));
    }

    [Test]
    [Arguments("""[{"a":{"S":"x"}},{"a":{"S":"y"},""")]
    [Arguments("""[{"a":{"S":"x"}},{"a":{"Q":"y"}}]""")]
    [Arguments("""[{"a":{"S":"x"}},{"a":{"S":"y"},"a":{"S":"z"}}]""")]
    [Arguments("""[{"a":{"S":"x"}},{"a":{"M":{"b":{"Q":"y"}}}}]""")]
    [Arguments("""[{"a":{"S":"x"}},{"a":{"L":[{"M":{"b":{"S":"y"},"b":{"S":"z"}}}]}}]""")]
    [Arguments("""[{"a":{"S":"x"}},{"b":{"SS":[]}}]""")]
    [Arguments("""[{"a":{"S":"x"}},5]""")]
    public async Task ReadItems_MalformedItemAfterAGoodOne_ReturnsEveryRentedArrayCleared(string json)
    {
        // Arrange
        var pool = new CountingArrayPool<Item>();
        var pools = TestPools.Create();

        // Act
        var act = () => TestJson.Read(json, (ref reader) => OperationJson.ReadItems(ref reader, pool, pools.Pools));
        await Assert.That(act).Throws<JsonException>();

        // Assert
        await Assert.That((pool.Outstanding, pools.Outstanding, pool.ReturnedUncleared, pools.Strings.ReturnedUncleared, pools.Values.ReturnedUncleared)).IsEqualTo((0, 0, false, false, false));
    }

    [Test]
    [Arguments("""{"a0":{"Q":"x"}}""")]
    [Arguments("""{"a1":{"S":"x"},""")]
    [Arguments("""{"a0":{"S":"x"},"a0":{"S":"y"}}""")]
    public async Task ReadItems_MalformedEndOfALargeItem_ReturnsEveryRentedArrayCleared(string end)
    {
        // Arrange (20 good attributes come first, so the names and values of the bad item are in rented arrays)
        ArgumentNullException.ThrowIfNull(end);
        var pool = new CountingArrayPool<Item>();
        var pools = TestPools.Create();
        var attributes = string.Join(',', Enumerable.Range(10, 20).Select(static i => $"\"a{i}\":{{\"N\":\"{i}\"}}"));
        var json = "[{" + attributes + "},{" + attributes + "," + end[1..];

        // Act
        var act = () => TestJson.Read(json, (ref reader) => OperationJson.ReadItems(ref reader, pool, pools.Pools));
        await Assert.That(act).Throws<JsonException>();

        // Assert
        await Assert.That((pool.Outstanding, pools.Outstanding, pools.Strings.ReturnedUncleared, pools.Values.ReturnedUncleared)).IsEqualTo((0, 0, false, false));
    }

    [Test]
    public async Task ReadItemCollectionMetrics_KeyAndRange_ReturnsBoth()
    {
        // Arrange
        const string json = """{"ItemCollectionKey":{"pk":{"S":"a"}},"SizeEstimateRangeGB":[0.0,1.0]}""";

        // Act
        var metrics = ReadMetrics(json);

        // Assert
        await Assert.That((metrics.ItemCollectionKey!["pk"].AsString(), metrics.SizeEstimateRangeGB![0], metrics.SizeEstimateRangeGB[1])).IsEqualTo(("a", 0.0, 1.0));
    }

    [Test]
    public async Task ReadItemCollectionMetrics_EmptyObject_HasNothing()
    {
        // Arrange
        const string json = "{}";

        // Act
        var metrics = ReadMetrics(json);

        // Assert
        await Assert.That((metrics.ItemCollectionKey, metrics.SizeEstimateRangeGB)).IsEqualTo(((IReadOnlyDictionary<string, AttributeValue>?)null, (IReadOnlyList<double>?)null));
    }

    [Test]
    public async Task ReadItemCollectionMetrics_UnknownMembers_AreSkipped()
    {
        // Arrange
        const string json = """{"Future":{"a":[1]},"SizeEstimateRangeGB":[2.5,3.5],"Other":1}""";

        // Act
        var metrics = ReadMetrics(json);

        // Assert
        await Assert.That(metrics.SizeEstimateRangeGB).IsEquivalentTo([2.5, 3.5]);
    }

    [Test]
    public async Task ReadItemCollectionMetrics_EmptyRange_ReturnsAnEmptyList()
    {
        // Arrange
        const string json = """{"SizeEstimateRangeGB":[]}""";

        // Act
        var metrics = ReadMetrics(json);

        // Assert
        await Assert.That(metrics.SizeEstimateRangeGB).IsEmpty();
    }

    [Test]
    public async Task ReadItemCollectionMetrics_RangeIsNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"SizeEstimateRangeGB":1.5}""";

        // Act
        var act = () => ReadMetrics(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessageContaining("Expected a JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadItemCollectionMetrics_BoundIsNotANumber_ThrowsInvalidOperationException()
    {
        // Arrange
        const string json = """{"SizeEstimateRangeGB":["1.5"]}""";

        // Act
        var act = () => ReadMetrics(json);

        // Assert
        await Assert.That(act).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ReadItemCollectionMetrics_KeyIsNotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"ItemCollectionKey":[]}""";

        // Act
        var act = () => ReadMetrics(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    [Test]
    public async Task ReadItemCollectionMetrics_NotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = "[]";

        // Act
        var act = () => ReadMetrics(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> ReadItems(string json, CountingArrayPool<Item> pool) =>
        TestJson.Read(json, (ref reader) => OperationJson.ReadItems(ref reader, pool, BuilderPools.Shared));

    private static ItemCollectionMetrics ReadMetrics(string json) =>
        TestJson.Read(json, static (ref reader) => OperationJson.ReadItemCollectionMetrics(ref reader));
}
