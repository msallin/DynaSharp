using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace DynaSharp.Benchmarks;

/// <content>
/// The reads that look at every item twice. The Mixed group reads one page of 100 items of two types with the discriminator as their last attribute, the worst place for it, against
/// the same page as one type. The FanOut group merges 4 partitions of 25 items each, against the same 4 responses as plain queries in a row, so that the difference is the merge.
/// </content>
public partial class TableBenchmarks
{
    private const int ItemsOfAPartition = 25;

    private static readonly AttributeValue[] FourPartitions = ["SHARD#0", "SHARD#1", "SHARD#2", "SHARD#3"];

    private CannedHandler _oneTypeHandler = null!;
    private CannedHandler _mixedHandler = null!;
    private CannedHandler _fanOutHandler = null!;
    private DynamoDbClient _oneType = null!;
    private DynamoDbClient _mixed = null!;
    private DynamoDbClient _fanOut = null!;
    private Table<Item> _mixedTable = null!;
    private Table<BenchOrder> _mixedOrders = null!;
    private Table<Item> _fanOutTable = null!;
    private Table<BenchOrder> _fanOutOrders = null!;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Mixed")]
    public async Task<int> Mixed_OneType()
    {
        var count = 0;
        await foreach (var unused in _mixedOrders.Query(PartitionKey).ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("Mixed")]
    public async Task<int> Mixed_TwoTypes()
    {
        var count = 0;
        await foreach (var unused in _mixedTable.Query(PartitionKey).Of<BenchOrder, BenchInvoice>().ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FanOut")]
    public async Task<int> FanOut_FourQueriesInARow()
    {
        var count = 0;
        foreach (var partition in FourPartitions)
        {
            await foreach (var unused in _fanOutOrders.Query(partition).ItemsAsync())
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("FanOut")]
    public async Task<int> FanOut_Typed()
    {
        var count = 0;
        await foreach (var unused in _fanOutOrders.Query(FourPartitions).ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("FanOut")]
    public async Task<int> FanOut_Item()
    {
        var count = 0;
        await foreach (var unused in _fanOutTable.Query(FourPartitions).ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("FanOut")]
    public async Task<int> FanOut_PageOf25() => (await _fanOutOrders.Query(FourPartitions).Limit(ItemsOfAPartition).PageAsync(ItemsOfAPartition)).Count;

    private static string Page(int items, Func<int, string> item)
    {
        var page = new StringBuilder("{\"Items\":[");
        for (var i = 0; i < items; i++)
        {
            page.Append(i == 0 ? string.Empty : ",").Append(item(i));
        }

        var size = items.ToString(CultureInfo.InvariantCulture);
        return page.Append("],\"Count\":").Append(size).Append(",\"ScannedCount\":").Append(size).Append('}').ToString();
    }

    private void SetupReads()
    {
        var typed = ItemAttributes[..ItemAttributes.LastIndexOf('}', StringComparison.Ordinal)].Replace("\"type\":{\"S\":\"Order\"},", string.Empty, StringComparison.Ordinal);
        _oneTypeHandler = new CannedHandler(Page(QueryPageSize, _ => typed + ",\"type\":{\"S\":\"Order\"}}"));
        _mixedHandler = new CannedHandler(Page(QueryPageSize, i => typed + ",\"type\":{\"S\":\"" + (i % 2 == 0 ? "Order" : "Invoice") + "\"}}"));
        _fanOutHandler = new CannedHandler(Page(ItemsOfAPartition, i => ItemAttributes.Replace(SortKey, SortKey + i.ToString("D3", CultureInfo.InvariantCulture), StringComparison.Ordinal)));
        _oneType = Create(_oneTypeHandler);
        _mixed = Create(_mixedHandler);
        _fanOut = Create(_fanOutHandler);
        _mixedTable = _mixed.Table(TableName, "pk", "sk");
        _mixedOrders = _oneType.Table<BenchOrder>(TableName);
        _fanOutTable = _fanOut.Table(TableName, "pk", "sk");
        _fanOutOrders = _fanOut.Table<BenchOrder>(TableName);
    }

    private void CleanupReads()
    {
        _oneType.Dispose();
        _mixed.Dispose();
        _fanOut.Dispose();
        _oneTypeHandler.Dispose();
        _mixedHandler.Dispose();
        _fanOutHandler.Dispose();
    }
}
