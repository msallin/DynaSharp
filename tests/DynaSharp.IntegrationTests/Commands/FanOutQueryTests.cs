using System.Globalization;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Mapping;
using DynaSharp.IntegrationTests.Support;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>
/// The fan-out query on the emulators. The merge must give the order DynamoDB gives, so that order comes from the emulator: all values go into one partition, whose query is the
/// expected result, and the same values go into shards, whose merge must equal it. So nothing here repeats the comparison of the code under test.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class FanOutQueryTests(Backend backend)
{
    private const string Oracle = "all";

    // More than any read here takes, so that a token that stops moving fails a test and does not hang it.
    private const int MostPages = 200;

    private static readonly AttributeValue[] Shards = ["shard-0", "shard-1", "shard-2"];

    private static readonly AttributeValue[] NoPartitions = [];

    // By code unit, code point and bytes these orders differ, so a merge by anything else than UTF-8 bytes shows up: 𝄞 is a surrogate pair below U+FFFD in UTF-16.
    private static readonly string[] Texts =
    [
        "a", "Z", "0", " ", "~", "ab", "a b", "aB", "é", "e", "z", "ß", "€", "�", "", "𝄞", "😀", "#", "ORDER#1", "ORDER#10", "ORDER#2", "\"quoted\"", "back\\slash",
    ];

    // Every neighbour here is what text order, a double or a decimal gets wrong.
    private static readonly string[] Numbers =
    [
        "-9.9999999999999999999999999999999999999E+125", "-1E+100", "-10000000000000000000000000000000000001", "-10000000000000000000000000000000000000", "-1000", "-100", "-99.5", "-10", "-9.5",
        "-1", "-0.5", "-0.1", "-0.09", "-1E-129", "0", "1E-130", "0.09", "0.1", "0.5", "1", "1.5", "9", "9.5", "10", "99", "100", "1000",
        "12345678901234567890123456789012345678", "12345678901234567890123456789012345679", "100000000000000000000000000000000000000", "1E+100", "9.9999999999999999999999999999999999999E+125",
    ];

    private static readonly byte[][] Bytes = [[0x00], [0x00, 0x00], [0x00, 0x01], [0x01], [0x7F], [0x80], [0xFB], [0xFB, 0xEF], [0xFF], [0xFF, 0x00], [0xFF, 0xFF]];

    [Test]
    public async Task ItemsAsync_StringSortKeys_GiveTheOrderOfOnePartition()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = sandbox.Client.Table(await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.String)), "pk", "sk");
        await PutInBothAsync(table, Texts.Select(static text => (AttributeValue)text));

        // Act
        var expected = await Read.ToListAsync(table.Query(Oracle).ItemsAsync());
        var merged = await Read.ToListAsync(table.Query(Shards).ItemsAsync());

        // Assert
        await Assert.That(Show(merged)).IsEqualTo(Show(expected));
    }

    [Test]
    public async Task ItemsAsync_NumberSortKeys_GiveTheOrderOfOnePartition()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = sandbox.Client.Table(await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Number)), "pk", "sk");
        await PutInBothAsync(table, Numbers.Select(AttributeValue.FromNumber));

        // Act
        var expected = await Read.ToListAsync(table.Query(Oracle).ItemsAsync());
        var merged = await Read.ToListAsync(table.Query(Shards).ItemsAsync());

        // Assert
        await Assert.That(Show(merged)).IsEqualTo(Show(expected));
    }

    [Test]
    public async Task ItemsAsync_BinarySortKeys_GiveTheOrderOfOnePartition()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = sandbox.Client.Table(await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Binary)), "pk", "sk");
        await PutInBothAsync(table, Bytes.Select(static value => AttributeValue.FromBinary(value)));

        // Act
        var expected = await Read.ToListAsync(table.Query(Oracle).ItemsAsync());
        var merged = await Read.ToListAsync(table.Query(Shards).ItemsAsync());

        // Assert
        await Assert.That(Show(merged)).IsEqualTo(Show(expected));
    }

    [Test]
    public async Task ItemsAsync_Descending_GivesTheOrderOfOnePartitionDescending()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = sandbox.Client.Table(await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Number)), "pk", "sk");
        await PutInBothAsync(table, Numbers.Select(AttributeValue.FromNumber));

        // Act
        var expected = await Read.ToListAsync(table.Query(Oracle).Descending().ItemsAsync());
        var merged = await Read.ToListAsync(table.Query(Shards).Descending().ItemsAsync());

        // Assert
        await Assert.That(Show(merged)).IsEqualTo(Show(expected));
    }

    [Test]
    public async Task ItemsAsync_EqualSortKeysAcrossPartitions_ComeByPositionAndDescendingReversesIt()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 2);
        await sandbox.PutOrdersAsync("b", 2);
        await sandbox.PutOrdersAsync("c", 2);
        var keys = new AttributeValue[] { "c", "a", "b" };

        // Act
        var ascending = await Read.ToListAsync(sandbox.Shop.Query(keys).ItemsAsync());
        var descending = await Read.ToListAsync(sandbox.Shop.Query(keys).Descending().ItemsAsync());

        // Assert
        await Assert.That((string.Join(",", ascending.Select(PartitionAndSort)), string.Join(",", descending.Select(PartitionAndSort))))
            .IsEqualTo(("c1,a1,b1,c2,a2,b2", "b2,a2,c2,b1,a1,c1"));
    }

    [Test]
    public async Task ItemsAsync_SortKeyCondition_HoldsInEveryPartition()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 5);
        await sandbox.PutOrdersAsync("b", 5);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(["a", "b"], SortKey.Between("ORDER#002", "ORDER#003")).ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Select(PartitionAndSort))).IsEqualTo("a2,b2,a3,b3");
    }

    [Test]
    public async Task ItemsAsync_FilterThatEmptiesPages_FollowsThem()
    {
        // Arrange: pages of one, and only the last item of a passes
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 6);
        await sandbox.PutOrdersAsync("b", 6);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(["a", "b"]).Limit(1).Filter(Attr("total").Eq(6)).ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Select(PartitionAndSort))).IsEqualTo("a6,b6");
    }

    [Test]
    public async Task ItemsAsync_GlobalIndex_MergesByTheSortKeyOfTheIndex()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3, "OPEN");
        await sandbox.PutOrdersAsync("b", 3, "CLOSED");
        var index = sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt");

        // Act
        var items = await Read.ToListAsync(index.Query(["OPEN", "CLOSED"]).Limit(1).ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Select(PartitionAndSort))).IsEqualTo("a1,b1,a2,b2,a3,b3");
    }

    [Test]
    public async Task PageAsync_GlobalIndex_TokensHoldTheKeyOfIndexAndTable()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 4, "OPEN");
        await sandbox.PutOrdersAsync("b", 3, "CLOSED");
        var index = sandbox.Shop.Index(ReadSandbox.GlobalIndex, "status", "placedAt");
        var keys = new AttributeValue[] { "OPEN", "CLOSED" };

        // Act
        var all = await ReadInPagesAsync((token, size) => index.Query(keys).PageAsync(size, token), 2);

        // Assert
        await Assert.That(string.Join(",", all.Select(PartitionAndSort))).IsEqualTo("a1,b1,a2,b2,a3,b3,a4");
    }

    [Test]
    public async Task PageAsync_LocalIndex_TokensHoldTheKeyOfIndexAndTable()
    {
        // Arrange: the rank falls as the sort key rises
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        await sandbox.PutOrdersAsync("b", 3);
        var index = sandbox.Shop.Index(ReadSandbox.LocalIndex, "pk", "rank");
        var keys = new AttributeValue[] { "a", "b" };

        // Act
        var first = await index.Query(keys).PageAsync(3);
        var second = await index.Query(keys).PageAsync(3, first.Next);

        // Assert
        await Assert.That(string.Join(",", first.Items.Concat(second.Items).Select(PartitionAndSort))).IsEqualTo("a3,b3,a2,b2,a1,b1");
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(7)]
    [Arguments(50)]
    public async Task PageAsync_AnyPageSize_ConcatenatesToTheUnpagedMerge(int pageSize)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = sandbox.Client.Table(await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.String)), "pk", "sk");
        await PutInBothAsync(table, Texts.Select(static text => (AttributeValue)text));
        var expected = await Read.ToListAsync(table.Query(Oracle).ItemsAsync());

        // Act
        var all = await ReadInPagesAsync((token, size) => table.Query(Shards).PageAsync(size, token), pageSize);

        // Assert
        await Assert.That(Show(all)).IsEqualTo(Show(expected));
    }

    [Test]
    public async Task PageAsync_NumberSortKeysDescendingInSmallPages_ConcatenatesToTheUnpagedMerge()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = sandbox.Client.Table(await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Number)), "pk", "sk");
        await PutInBothAsync(table, Numbers.Select(AttributeValue.FromNumber));
        var expected = await Read.ToListAsync(table.Query(Oracle).Descending().ItemsAsync());

        // Act
        var all = await ReadInPagesAsync((token, size) => table.Query(Shards).Descending().Limit(2).PageAsync(size, token), 3);

        // Assert
        await Assert.That(Show(all)).IsEqualTo(Show(expected));
    }

    [Test]
    public async Task PageAsync_BinarySortKeysInSmallPages_ConcatenatesToTheUnpagedMerge()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = sandbox.Client.Table(await sandbox.CreateTableAsync(new TableSpec(SortKeyType: KeyAttributeType.Binary)), "pk", "sk");
        await PutInBothAsync(table, Bytes.Select(static value => AttributeValue.FromBinary(value)));
        var expected = await Read.ToListAsync(table.Query(Oracle).ItemsAsync());

        // Act
        var all = await ReadInPagesAsync((token, size) => table.Query(Shards).PageAsync(size, token), 2);

        // Assert
        await Assert.That(Show(all)).IsEqualTo(Show(expected));
    }

    [Test]
    public async Task PageAsync_Filter_ConcatenatesToWhatPasses()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 6);
        await sandbox.PutOrdersAsync("b", 6);
        var keys = new AttributeValue[] { "a", "b" };

        // Act
        var all = await ReadInPagesAsync((token, size) => sandbox.Shop.Query(keys).Filter(Attr("kind").Eq("even")).PageAsync(size, token), 2);

        // Assert
        await Assert.That(string.Join(",", all.Select(PartitionAndSort))).IsEqualTo("a2,b2,a4,b4,a6,b6");
    }

    [Test]
    public async Task ItemsAsync_LeftAfterTheFirstItem_ReadsOnePagePerPartition()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 6);
        await sandbox.PutOrdersAsync("b", 6);
        await sandbox.PutOrdersAsync("c", 6);
        using var counted = new CountedClient(sandbox.Emulator);

        // Act
        await foreach (var unused in counted.Table(sandbox.TableName).Query(["a", "b", "c"]).Consistent().Limit(2).ItemsAsync())
        {
            break;
        }

        // Assert
        await Assert.That(counted.Requests).IsEqualTo(3);
    }

    [Test]
    public async Task ItemsAsync_APartitionThatSortsLater_IsNotReadBeforeItsTurn()
    {
        // Arrange: b holds the sort keys above what a holds, so its second page waits until a is read to the end
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        for (var i = 1; i <= 6; i++)
        {
            await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = ReadSandbox.OrderKey(i) }).SendAsync();
            await sandbox.Shop.Put(new Item { ["pk"] = "b", ["sk"] = ReadSandbox.OrderKey(i + 100) }).SendAsync();
        }

        using var counted = new CountedClient(sandbox.Emulator);
        var seen = 0;
        var sentAtTheLastItemOfA = 0;

        // Act
        await foreach (var unused in counted.Table(sandbox.TableName).Query(["a", "b"]).Consistent().Limit(2).ItemsAsync())
        {
            seen++;
            if (seen == 6)
            {
                sentAtTheLastItemOfA = counted.Requests;
            }
        }

        // Assert (the first page of each, and the second and third page of a)
        await Assert.That((seen, sentAtTheLastItemOfA)).IsEqualTo((12, 4));
    }

    [Test]
    public async Task PageAsync_TimeBuckets_ReadsOnlyTheBucketThePagesAreIn()
    {
        // Arrange: 4 days of 4 items, every day after the one before, in pages of 2. A partition that is not read before its turn costs nothing but its first read.
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        for (var day = 0; day < 4; day++)
        {
            for (var item = 0; item < 4; item++)
            {
                await sandbox.Shop.Put(new Item { ["pk"] = "day" + day.ToString(CultureInfo.InvariantCulture), ["sk"] = ReadSandbox.OrderKey((day * 10) + item) }).SendAsync();
            }
        }

        using var counted = new CountedClient(sandbox.Emulator);
        string[] days = ["day0", "day1", "day2", "day3"];
        var table = counted.Table(sandbox.TableName);
        var afterFirstPage = 0;

        // Act
        var all = await ReadInPagesAsync(
            (token, size) =>
            {
                afterFirstPage = token.HasValue ? afterFirstPage : counted.Requests;
                return table.Query(days).Consistent().PageAsync(size, token);
            },
            2);

        // Assert (4 for the first page, then the 2 reads of a day that a page is in with the empty end of the day before: fewer than one read per page, per day that is left)
        await Assert.That((all.Count, counted.Requests < 4 + 16, Read.SortKeys(all).Split(',').Order(StringComparer.Ordinal).SequenceEqual(Read.SortKeys(all).Split(',')))).IsEqualTo((16, true, true));
    }

    [Test]
    public async Task PageAsync_ReturnCapacity_AddsUpTheCallsOfThePage()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        await sandbox.PutOrdersAsync("b", 3);
        string[] keys = ["a", "b"];

        // Act
        var one = await sandbox.Shop.Query("a").ReturnCapacity().PageAsync();
        var both = await sandbox.Shop.Query(keys).ReturnCapacity().MaxAttempts(1).PageAsync(6);

        // Assert (each partition costs at least what one query costs, so two cost at least twice that)
        await Assert.That(both.ConsumedCapacity!.CapacityUnits >= 2 * one.ConsumedCapacity!.CapacityUnits).IsTrue();
    }

    [Test]
    public async Task ItemsAsync_StringKeys_ReadThePartitions()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 2);
        await sandbox.PutOrdersAsync("b", 2);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(["a", "b"]).ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", items.Select(PartitionAndSort))).IsEqualTo("a1,b1,a2,b2");
    }

    [Test]
    public async Task ItemsAsync_NoPartitions_SendsNothing()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        using var counted = new CountedClient(sandbox.Emulator);

        // Act
        var items = await Read.ToListAsync(counted.Table(sandbox.TableName).Query(NoPartitions).ItemsAsync());

        // Assert
        await Assert.That((items.Count, counted.Requests)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task CountAsync_Partitions_SumsThem()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 3);
        await sandbox.PutOrdersAsync("b", 4);

        // Act
        var all = await sandbox.Shop.Query(["a", "b", "empty"]).CountAsync();
        var filtered = await sandbox.Shop.Query(["a", "b"]).Filter(Attr("kind").Eq("even")).Limit(2).CountAsync();

        // Assert
        await Assert.That((all, filtered)).IsEqualTo((7, 3));
    }

    [Test]
    public async Task ItemsAsync_EntityOverCustomers_MergesTheirOrders()
    {
        // Arrange: orders of write shards, read as the entity
        await using var shop = await ShopSandbox.StartAsync(backend);
        var noon = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 6; i++)
        {
            await shop.Orders.Put(new Order { CustomerId = "s" + (i % 2).ToString(CultureInfo.InvariantCulture), PlacedAt = noon.AddHours(i), OrderId = Guid.NewGuid(), Status = OrderStatus.Paid, Total = i }).SendAsync();
        }

        await shop.Customers.Put(new Customer { Id = "s0", Name = "Not an order" }).SendAsync();
        var shards = new AttributeValue[] { Order.Keys.Pk.Format("s0"), Order.Keys.Pk.Format("s1") };

        // Act
        var orders = await Read.ToListAsync(shop.Orders.Query(shards, SortKey.BeginsWith(Order.Keys.Sk.Prefix())).Descending().ItemsAsync());

        // Assert
        await Assert.That(string.Join(",", orders.Select(static order => order.Total))).IsEqualTo("5,4,3,2,1,0");
    }

    [Test]
    public async Task ItemsAsync_Projection_ReadsOnlyWhatWasAskedAndTheKeys()
    {
        // Arrange
        await using var sandbox = await ReadSandbox.StartAsync(backend);
        await sandbox.PutOrdersAsync("a", 2);
        await sandbox.PutOrdersAsync("b", 2);

        // Act
        var items = await Read.ToListAsync(sandbox.Shop.Query(["a", "b"]).Project("total").ItemsAsync());

        // Assert
        await Assert.That((items.Count, items.All(static item => item.Count == 3))).IsEqualTo((4, true));
    }

    private static string Show(IEnumerable<Item> items) => string.Join("|", items.Select(static item => item["sk"].ToString()));

    private static string PartitionAndSort(Item item) => item.GetString("pk") + item.GetString("sk")![^1];

    // A client that pages: every page is a new query and the token goes through its text.
    private static async Task<List<Item>> ReadInPagesAsync(Func<ContinuationToken, int, ValueTask<Page<Item>>> read, int pageSize)
    {
        var all = new List<Item>();
        var token = string.Empty;
        var pages = 0;
        do
        {
            var page = await read(ContinuationToken.Parse(token), pageSize);
            all.AddRange(page.Items);
            token = page.Next.ToString();
            pages++;
        }
        while (token.Length > 0 && pages < MostPages);

        return all;
    }

    private static async Task PutInBothAsync(Table<Item> table, IEnumerable<AttributeValue> sortKeys)
    {
        var index = 0;
        foreach (var sortKey in sortKeys)
        {
            await table.Put(new Item { ["pk"] = Oracle, ["sk"] = sortKey }).SendAsync();
            await table.Put(new Item { ["pk"] = Shards[index++ % Shards.Length], ["sk"] = sortKey }).SendAsync();
        }
    }

    // A client of the same backend that counts what goes over the wire. Its reads are consistent: real DynamoDB may still be replicating the writes of the sandbox, which this client knows nothing of.
    private sealed class CountedClient : IDisposable
    {
        private readonly CountingHandler _handler = new();
        private readonly DynamoDbClient _client;

        public CountedClient(Emulator emulator)
        {
            _client = new DynamoDbClient(emulator.CreateOptions() with { MaxAttempts = 1, HttpHandler = _handler });
        }

        public int Requests => _handler.Requests;

        public Table<Item> Table(string name) => _client.Table(name, "pk", "sk");

        public void Dispose()
        {
            _client.Dispose();
            _handler.Dispose();
        }

        private sealed class CountingHandler : DelegatingHandler
        {
            private int _requests;

#pragma warning disable CA2000 // The base class takes the handler and disposes it.
            public CountingHandler()
                : base(new SocketsHttpHandler())
#pragma warning restore CA2000
            {
            }

            public int Requests => Volatile.Read(ref _requests);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _requests);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
