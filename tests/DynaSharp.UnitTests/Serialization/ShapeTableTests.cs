using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class ShapeTableTests
{
    [Test]
    public async Task CreateItem_SameNames_SharesTheShapeWithoutAPreviousItem()
    {
        // Arrange (single reads, such as GetItem, have only the table)
        var pools = TestPools.Create();

        // Act
        var first = pools.Read("""{"a":{"N":"1"},"b":{"N":"2"}}""");
        var second = pools.Read("""{"a":{"N":"3"},"b":{"N":"4"}}""");

        // Assert
        await Assert.That(ReferenceEquals(first.Shape, second.Shape)).IsTrue();
    }

    [Test]
    public async Task CreateItem_SameNamesInAnotherOrder_SharesTheShapeAndKeepsEachValueWithItsName()
    {
        // Arrange
        var pools = TestPools.Create();

        // Act
        var first = pools.Read("""{"a":{"N":"1"},"b":{"N":"2"},"c":{"N":"3"}}""");
        var second = pools.Read("""{"c":{"N":"6"},"a":{"N":"4"},"b":{"N":"5"}}""");

        // Assert
        await Assert.That((ReferenceEquals(first.Shape, second.Shape), second["a"].AsNumber(), second["b"].AsNumber(), second["c"].AsNumber())).IsEqualTo((true, "4", "5", "6"));
    }

    [Test]
    public async Task CreateItem_OtherNames_HasItsOwnShape()
    {
        // Arrange
        var pools = TestPools.Create();

        // Act
        var first = pools.Read("""{"a":{"N":"1"}}""");
        var second = pools.Read("""{"b":{"N":"1"}}""");

        // Assert
        await Assert.That((ReferenceEquals(first.Shape, second.Shape), second.Single().Key)).IsEqualTo((false, "b"));
    }

    [Test]
    public async Task CreateItem_NoAttributes_NeedsNoTable()
    {
        // Arrange
        var pools = TestPools.Create();

        // Act
        var item = pools.Read("{}");

        // Assert
        await Assert.That(ReferenceEquals(item.Shape, ItemShape.Empty)).IsTrue();
    }

    [Test]
    public async Task CreateItem_DuplicateName_ThrowsJsonExceptionEachTime()
    {
        // Arrange (the same duplicate twice: a failed read must not leave a shape behind that lets it through)
        var pools = TestPools.Create();
        const string json = """{"a":{"N":"1"},"a":{"N":"2"}}""";

        // Act
        var first = () => pools.Read(json);
        var second = () => pools.Read(json);

        // Assert
        await Assert.That(first).ThrowsExactly<JsonException>();
        await Assert.That(second).ThrowsExactly<JsonException>();
    }

    [Test]
    public async Task CreateItem_DuplicateNameOverAKnownShapeOfThatSize_ThrowsJsonException()
    {
        // Arrange (a is known, and the item has as many attributes as the shape has)
        var pools = TestPools.Create();
        pools.Read("""{"a":{"N":"1"},"b":{"N":"2"}}""");

        // Act
        var act = () => pools.Read("""{"a":{"N":"1"},"a":{"N":"2"}}""");

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>()
            .WithMessage("The attribute name \"a\" occurs more than once.", StringComparison.Ordinal);
    }

    [Test]
    public async Task CreateItem_NamesWhoseHashesCollide_KeepsTheShapesApart()
    {
        // Arrange (hashes are not randomized, so such names can be found, and chosen on purpose)
        var (first, second) = CollidingNamePairs();
        var pools = TestPools.Create();

        // Act
        var one = pools.Read(Json(first));
        var other = pools.Read(Json(second));

        // Assert
        await Assert.That((ReferenceEquals(one.Shape, other.Shape), one.ContainsKey(first[0]), other.ContainsKey(second[0]), other.ContainsKey(first[0]))).IsEqualTo((false, true, true, false));
    }

    [Test]
    public async Task CreateItem_NamesWhoseHashesCollideWithThePreviousItem_KeepsTheShapesApart()
    {
        // Arrange
        var (first, second) = CollidingNamePairs();
        var pools = TestPools.Create();
        var previous = pools.Read(Json(first));

        // Act
        var item = ReadWithPrevious(pools, Json(second), previous.Shape);

        // Assert
        await Assert.That((item.ContainsKey(second[0]), item.ContainsKey(first[0]))).IsEqualTo((true, false));
    }

    [Test]
    public async Task CreateItem_NoFreeSlotAmongThoseItMayUse_ReplacesOneShapeAndKeepsTheOthers()
    {
        // Arrange (four slots, which every shape may use; the fifth replaces exactly one)
        var pools = TestPools.Create(NameTable.Probes);
        var shapes = "abcd".Select(name => pools.Read(Json([name.ToString()])).Shape).ToArray();
        pools.Read(Json(["e"]));

        // Act
        var again = "abcd".Select(name => pools.Read(Json([name.ToString()])).Shape);

        // Assert
        await Assert.That(shapes.Zip(again, ReferenceEquals).Count(static same => same)).IsEqualTo(3);
    }

    [Test]
    public async Task CreateItem_MoreShapesThanSlots_ReadsEveryItemCorrectly()
    {
        // Arrange
        var pools = TestPools.Create(4);

        // Act
        var names = Enumerable.Range(0, 1000).Select(static i => $"name{i}").ToArray();
        var items = names.Select(name => pools.Read(Json([name, "common"]))).ToArray();

        // Assert
        await Assert.That(items.Zip(names).All(static pair => pair.First.ContainsKey(pair.Second) && pair.First.ContainsKey("common"))).IsTrue();
    }

    [Test]
    public async Task CreateItem_AttackerChosenShapes_RetainsNoMoreThanTheSlots()
    {
        // Arrange (retained are the shapes that come back as the same instance)
        const int slots = 64;
        var pools = TestPools.Create(slots);
        var items = Enumerable.Range(0, 20_000).Select(static i => Json([$"attacker{i}", "pk"])).ToArray();
        var firstPass = items.Select(json => pools.Read(json).Shape).ToArray();

        // Act
        var secondPass = items.Select(json => pools.Read(json).Shape).ToArray();

        // Assert
        await Assert.That(firstPass.Zip(secondPass, ReferenceEquals).Count(static same => same)).IsLessThanOrEqualTo(slots);
    }

    [Test]
    public async Task CreateItem_ShapeAboveTheRetainedLimit_IsNotKeptButTheNextItemOfThePageSharesIt()
    {
        // Arrange (400 names of 90 characters each hold 36 KB of text, which the table must not retain)
        var pools = TestPools.Create();
        var names = Enumerable.Range(0, 400).Select(static i => $"{i}".PadLeft(90, 'n')).ToArray();
        var json = Json(names);
        var first = pools.Read(json);

        // Act
        var secondRead = pools.Read(json);
        var nextInPage = ReadWithPrevious(pools, json, first.Shape);

        // Assert
        await Assert.That((first.Shape.RetainedBytes > 8 * 1024, ReferenceEquals(first.Shape, secondRead.Shape), ReferenceEquals(first.Shape, nextInPage.Shape))).IsEqualTo((true, false, true));
    }

    [Test]
    public async Task CreateItem_ShapeAtTheRetainedLimit_IsKept()
    {
        // Arrange (8 KB exactly: 80 names of 18 characters cost 100 bytes each, and a name of 64 characters costs the remaining 192)
        var pools = TestPools.Create();
        var names = Enumerable.Range(0, 80).Select(static i => $"{i}".PadLeft(18, 'n')).Append(new string('x', 64)).ToArray();
        var first = pools.Read(Json(names));

        // Act
        var second = pools.Read(Json(names));

        // Assert
        await Assert.That((first.Shape.RetainedBytes, ReferenceEquals(first.Shape, second.Shape))).IsEqualTo((8 * 1024, true));
    }

    [Test]
    public async Task CreateItem_ShapeJustAboveTheRetainedLimit_IsNotKept()
    {
        // Arrange
        var pools = TestPools.Create();
        var names = Enumerable.Range(0, 80).Select(static i => $"{i}".PadLeft(18, 'n')).Append(new string('x', 65)).ToArray();
        var first = pools.Read(Json(names));

        // Act
        var second = pools.Read(Json(names));

        // Assert
        await Assert.That((first.Shape.RetainedBytes, ReferenceEquals(first.Shape, second.Shape))).IsEqualTo(((8 * 1024) + 2, false));
    }

    [Test]
    [Arguments(10, 0)]
    [Arguments(10, 1)]
    [Arguments(10, 2)]
    [Arguments(40, 0)]
    [Arguments(40, 1)]
    [Arguments(40, 2)]
    public async Task ReadAttributes_ShapeHits_AllocatesTheItemAndItsValuesOnly(int attributes, int route)
    {
        // Arrange (booleans share a box, so anything else would come from the reader; the pools of the larger size are warm after the first read, and names have room)
        var tables = new BuilderPools(ArrayPool<AttributeValue>.Shared, ArrayPool<string>.Shared, ArrayPool<ReadOnlyMemory<byte>>.Shared, new NameTable(4096), new ShapeTable(64));
        var inOrder = BooleanItem(Enumerable.Range(0, attributes));
        var shuffled = BooleanItem(Enumerable.Range(0, attributes).Reverse());
        var known = Read(inOrder, tables, null).Shape;
        var (json, previous) = route switch
        {
            0 => (inOrder, known),   // follows the previous item
            1 => (inOrder, null),    // found in the table
            _ => (shuffled, known),  // the previous item, in another order
        };

        // Act (the lowest of a few passes, in case something else runs on this thread, and each pass averages 100 reads)
        var bytesPerRead = long.MaxValue;
        for (var pass = 0; pass < 5; pass++)
        {
            bytesPerRead = Math.Min(bytesPerRead, AllocatedPerRead(json, tables, previous));
        }

        // Assert (an Item of 40 bytes, an array of 24 bytes and 16 bytes per value)
        await Assert.That(bytesPerRead).IsEqualTo(40 + 24 + (16L * attributes));
    }

    [Test]
    public async Task ReadItems_ManyThreadsOverCollidingTables_EveryItemHasItsOwnNamesAndValues()
    {
        // Arrange (a few slots for many shapes, so that threads replace each other's entries all the time)
        var pools = new BuilderPools(ArrayPool<AttributeValue>.Shared, ArrayPool<string>.Shared, ArrayPool<ReadOnlyMemory<byte>>.Shared, new NameTable(NameTable.Probes), new ShapeTable(NameTable.Probes));

        // Act
        var failures = RunOnAllCores(pools);

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task ReadItems_ManyThreadsOverTheProcessWideTables_EveryItemHasItsOwnNamesAndValues()
    {
        // Act
        var failures = RunOnAllCores(BuilderPools.Shared);

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    private static int RunOnAllCores(BuilderPools pools)
    {
        var threads = Math.Max(Environment.ProcessorCount, 4);
        using var start = new Barrier(threads);
        var failures = 0;
        var workers = Enumerable.Range(0, threads).Select(thread => new Thread(() =>
        {
            start.SignalAndWait();
            Interlocked.Add(ref failures, ReadMany(pools, thread));
        })).ToArray();
        Run(workers);
        return failures;
    }

    private static void Run(Thread[] workers)
    {
        foreach (var worker in workers)
        {
            worker.Start();
        }

        foreach (var worker in workers)
        {
            worker.Join();
        }
    }

    // Each page mixes shapes of different sizes on both sides of the index limit, in both orders. The value of every attribute spells its
    // name, so a value that lands under the wrong name, or a shape that loses a name, shows up. A duplicate must fail every time.
    private static int ReadMany(BuilderPools pools, int thread)
    {
        const int rounds = 300;
        const int shapes = 40;
        var failures = 0;
        for (var round = 0; round < rounds; round++)
        {
            var shape = (thread + round) % shapes;
            var names = Enumerable.Range(0, 2 + (shape % 12)).Select(i => string.Create(CultureInfo.InvariantCulture, $"s{shape}a{i}")).ToArray();
            var order = round % 2 == 0 ? names : names.AsEnumerable().Reverse();
            var json = "[" + string.Join(',', Enumerable.Repeat(SelfDescribing(order), 3)) + "," + SelfDescribing(names) + "]";
            var reader = new DynamoDbReader(Encoding.UTF8.GetBytes(json));
            var items = OperationJson.ReadItems(ref reader, ArrayPool<Item>.Shared, pools);
            foreach (var item in items)
            {
                failures += item.Count == names.Length && names.All(name => item[name].AsString() == name) ? 0 : 1;
            }

            failures += RejectsDuplicate(pools, shape) ? 0 : 1;
        }

        return failures;
    }

    private static bool RejectsDuplicate(BuilderPools pools, int shape)
    {
        var json = Encoding.UTF8.GetBytes(SelfDescribing([$"s{shape}a0", $"s{shape}a0"]));
        try
        {
            var reader = new DynamoDbReader(json);
            AttributeValueConverter.ReadItem(ref reader, pools);
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string SelfDescribing(IEnumerable<string> names) =>
        "{" + string.Join(',', names.Select(static name => $"\"{name}\":{{\"S\":\"{name}\"}}")) + "}";

    private static long AllocatedPerRead(byte[] json, BuilderPools pools, ItemShape? previous)
    {
        const int reads = 100;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < reads; i++)
        {
            Read(json, pools, previous);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / reads;
    }

    private static Item Read(byte[] json, BuilderPools pools, ItemShape? previous)
    {
        var reader = new DynamoDbReader(json);
        reader.ReadStartItem();
        return AttributeValueConverter.ReadAttributes(ref reader, pools, previous);
    }

    private static Item ReadWithPrevious(TestPools pools, string json, ItemShape previous) =>
        Read(Encoding.UTF8.GetBytes(json), pools.Pools, previous);

    private static byte[] BooleanItem(IEnumerable<int> attributes) =>
        Encoding.UTF8.GetBytes("{" + string.Join(',', attributes.Select(static i => $"\"attribute{i}\":{{\"BOOL\":true}}")) + "}");

    private static string Json(IEnumerable<string> names) =>
        "{" + string.Join(',', names.Select(static name => $"\"{name}\":{{\"N\":\"1\"}}")) + "}";

    // Two pairs of different names with equal sums of hashes, so items with them hash alike. Found by the birthday bound: about 100000 pairs.
    private static (string[] First, string[] Second) CollidingNamePairs()
    {
        var names = Enumerable.Range(0, 2000).Select(static i => string.Create(CultureInfo.InvariantCulture, $"n{i}")).ToArray();
        var seen = new Dictionary<int, string[]>();
        for (var i = 0; i < names.Length; i++)
        {
            for (var j = i + 1; j < names.Length; j++)
            {
                string[] pair = [names[i], names[j]];
                if (seen.TryGetValue(ItemShape.HashNames(pair), out var other) && !other.Intersect(pair).Any())
                {
                    return (other, pair);
                }

                seen[ItemShape.HashNames(pair)] = pair;
            }
        }

        throw new InvalidOperationException("No colliding pairs among these names.");
    }
}
