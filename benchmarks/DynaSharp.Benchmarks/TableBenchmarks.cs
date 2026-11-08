using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using DynaSharp.Credentials;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using static DynaSharp.Expr;

namespace DynaSharp.Benchmarks;

/// <summary>
/// The command API over canned HTTP, against the request API on the same handler. A call builds what it needs, as a caller does; the entity and its
/// strings exist already, so what is counted is the cost of the API. The body group stops before the pipeline, so it shows the cost of
/// building a body alone. The targets, measured when the command API was chosen: 1.41 KB for a write, 2.44 KB for a read of an
/// <see cref="Item"/> and 2.16 KB for a typed read, and 300 to 400 ns for the update body. The Query group reads one last page of 100 items, as the request API
/// does at 98.1 KB. The Paging group reads ten pages that a filter left empty, so that what is left is the cost of a page: its <c>Next</c> group mate
/// is one page, which <see cref="Paging_OneEmptyPage"/> subtracts. A transaction of 2 puts is 2.04 KB with fresh items of 3 and 4
/// attributes and 1.41 KB with entities the caller holds, a batch of 25 is 6.30 KB with fresh items of 3 attributes and 1.41 KB, and a batch of 60, which takes
/// 3 requests, is 15.96 KB and 4.24 KB. The items of the untyped rows are the object of the caller, not of the path. The Suspending group answers from a handler that
/// yields, which moves the state machines to the heap as a network does: 1.55 KB for a put of 8 attributes and 1.67 KB for a get of 3. The read of 3 of 10 attributes of a
/// page is 98.56 KB untyped and 70.39 KB typed. There is no target for Delete, BatchGet and TransactGet, so these rows stand on their own.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet needs an unsealed type and owns the lifetime through GlobalCleanup.")]
public partial class TableBenchmarks
{
    private const string TableName = "Orders";
    private const string PartitionKey = "USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19";
    private const string SortKey = "ORDER#2026-10-31T12:34:56.789Z";
    private const int QueryPageSize = 100;
    private const int FollowedPages = 10;
    private const int BatchGetKeys = 25;

    private const string ItemAttributes = """
        {"pk":{"S":"USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19"},"sk":{"S":"ORDER#2026-10-31T12:34:56.789Z"},"type":{"S":"Order"},
        "orderId":{"N":"9182736455"},"total":{"N":"1249.95"},"currency":{"S":"CHF"},"shipped":{"BOOL":true},"giftNote":{"NULL":true},
        "tags":{"SS":["priority","fragile","insured"]},"address":{"M":{"street":{"S":"Bahnhofstrasse 1"},"city":{"S":"Zürich"},"zip":{"S":"8001"}}}}
        """;

    // The item of the suspending get of the proposal.
    private const string SmallItemAttributes = """{"pk":{"S":"USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19"},"sk":{"S":"PROFILE"},"fullName":{"S":"Erin Early"}}""";

    private static readonly Attr<long, NumberConverter<long>> OrderId = Attr<long>("orderId");
    private static readonly string[] BatchKeys = [.. Enumerable.Range(0, 60).Select(static i => i.ToString("D4", CultureInfo.InvariantCulture))];
    private static readonly Attr<string, StringConverter> Currency = new("currency");

    private readonly BenchOrder _order = new()
    {
        Pk = PartitionKey,
        Sk = SortKey,
        OrderId = 9182736455,
        Total = 1249.95m,
        Currency = "CHF",
        Shipped = true,
        Tags = ["priority", "fragile", "insured"],
        Address = new BenchAddress("Bahnhofstrasse 1", "Zürich", "8001"),
    };

    private CannedHandler _getHandler = null!;
    private CannedHandler _writeHandler = null!;
    private DynamoDbClient _get = null!;
    private DynamoDbClient _write = null!;
    private Table<Item> _getTable = null!;
    private Table<Item> _writeTable = null!;
    private Table<BenchOrder> _getTyped = null!;
    private Table<BenchOrder> _writeTyped = null!;
    private GetItemRequest _getRequest = null!;
    private UpdateItemRequest _updateRequest = null!;
    private CannedHandler _pageHandler = null!;
    private CannedHandler _lastPageHandler = null!;
    private CannedHandler _countHandler = null!;
    private CannedPagesHandler _emptyPagesHandler = null!;
    private CannedHandler _emptyLastPageHandler = null!;
    private DynamoDbClient _page = null!;
    private DynamoDbClient _lastPage = null!;
    private DynamoDbClient _count = null!;
    private DynamoDbClient _emptyPages = null!;
    private DynamoDbClient _emptyLastPage = null!;
    private Table<Item> _pageTable = null!;
    private Table<Item> _lastPageTable = null!;
    private Table<BenchOrder> _lastPageTyped = null!;
    private Table<Item> _countTable = null!;
    private Table<Item> _emptyPagesTable = null!;
    private Table<Item> _emptyLastPageTable = null!;
    private QueryRequest _queryRequest = null!;
    private PutItemRequest _putRequest = null!;
    private DeleteItemRequest _deleteRequest = null!;
    private CannedHandler _counterHandler = null!;
    private CannedHandler _newItemHandler = null!;
    private CannedHandler _batchGetHandler = null!;
    private CannedHandler _transactGetHandler = null!;
    private CannedHandler _suspendingWriteHandler = null!;
    private CannedHandler _suspendingGetHandler = null!;
    private DynamoDbClient _counter = null!;
    private DynamoDbClient _newItem = null!;
    private DynamoDbClient _batchGet = null!;
    private DynamoDbClient _transactGet = null!;
    private DynamoDbClient _suspendingWrite = null!;
    private DynamoDbClient _suspendingGet = null!;
    private Table<Item> _counterTable = null!;
    private Table<BenchOrder> _newItemTyped = null!;
    private Table<Item> _batchGetTable = null!;
    private Table<BenchOrder> _batchGetTyped = null!;
    private Table<Item> _transactGetTable = null!;
    private Table<BenchOrder> _transactGetTyped = null!;
    private Table<Item> _suspendingWriteTable = null!;
    private Table<Item> _suspendingGetTable = null!;

    [GlobalSetup]
    public void Setup()
    {
        _getHandler = new CannedHandler("{\"Item\":" + ItemAttributes + "}");
        _writeHandler = new CannedHandler("{}");
        _get = Create(_getHandler);
        _write = Create(_writeHandler);
        _getTable = _get.Table(TableName, "pk", "sk");
        _writeTable = _write.Table(TableName, "pk", "sk");
        _getTyped = _get.Table<BenchOrder>(TableName);
        _writeTyped = _write.Table<BenchOrder>(TableName);
        _getRequest = new GetItemRequest { TableName = TableName, Key = new Dictionary<string, AttributeValue> { ["pk"] = PartitionKey, ["sk"] = SortKey } };
        _updateRequest = new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue> { ["pk"] = PartitionKey, ["sk"] = SortKey },
            UpdateExpression = "ADD #views :one SET #status = :paid",
            ConditionExpression = "#status = :open",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#views"] = "views", ["#status"] = "status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":one"] = AttributeValue.FromNumber(1), [":paid"] = "paid", [":open"] = "open" },
        };
        var key = "{\"pk\":{\"S\":\"" + PartitionKey + "\"},\"sk\":{\"S\":\"" + SortKey + "\"}}";
        var emptyPage = "{\"Items\":[],\"Count\":0,\"ScannedCount\":100,\"LastEvaluatedKey\":" + key + "}";
        const string EmptyLastPage = """{"Items":[],"Count":0,"ScannedCount":100}""";
        _pageHandler = new CannedHandler(QueryPage(",\"LastEvaluatedKey\":" + key));
        _lastPageHandler = new CannedHandler(QueryPage(string.Empty));
        _countHandler = new CannedHandler("{\"Count\":100,\"ScannedCount\":100}");
        _emptyPagesHandler = new CannedPagesHandler([.. Enumerable.Repeat(emptyPage, FollowedPages - 1), EmptyLastPage]);
        _emptyLastPageHandler = new CannedHandler(EmptyLastPage);
        _page = Create(_pageHandler);
        _lastPage = Create(_lastPageHandler);
        _count = Create(_countHandler);
        _emptyPages = Create(_emptyPagesHandler);
        _emptyLastPage = Create(_emptyLastPageHandler);
        _pageTable = _page.Table(TableName, "pk", "sk");
        _lastPageTable = _lastPage.Table(TableName, "pk", "sk");
        _lastPageTyped = _lastPage.Table<BenchOrder>(TableName);
        _countTable = _count.Table(TableName, "pk", "sk");
        _emptyPagesTable = _emptyPages.Table(TableName, "pk", "sk");
        _emptyLastPageTable = _emptyLastPage.Table(TableName, "pk", "sk");
        _queryRequest = new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = PartitionKey, [":prefix"] = "ORDER#" },
            Limit = QueryPageSize,
        };
        _putRequest = new PutItemRequest { TableName = TableName, Item = FreshItem() };
        _deleteRequest = new DeleteItemRequest { TableName = TableName, Key = new Dictionary<string, AttributeValue> { ["pk"] = PartitionKey, ["sk"] = SortKey } };
        _counterHandler = new CannedHandler("""{"Attributes":{"views":{"N":"8"}}}""");
        _newItemHandler = new CannedHandler("{\"Attributes\":" + ItemAttributes + "}");
        _batchGetHandler = new CannedHandler(BatchGetResponse());
        _transactGetHandler = new CannedHandler("{\"Responses\":[{\"Item\":" + ItemAttributes + "},{\"Item\":" + ItemAttributes + "}]}");
        _suspendingWriteHandler = new CannedHandler("{}", suspend: true);
        _suspendingGetHandler = new CannedHandler("{\"Item\":" + SmallItemAttributes + "}", suspend: true);
        _counter = Create(_counterHandler);
        _newItem = Create(_newItemHandler);
        _batchGet = Create(_batchGetHandler);
        _transactGet = Create(_transactGetHandler);
        _suspendingWrite = Create(_suspendingWriteHandler);
        _suspendingGet = Create(_suspendingGetHandler);
        _counterTable = _counter.Table(TableName, "pk", "sk");
        _newItemTyped = _newItem.Table<BenchOrder>(TableName);
        _batchGetTable = _batchGet.Table(TableName, "pk", "sk");
        _batchGetTyped = _batchGet.Table<BenchOrder>(TableName);
        _transactGetTable = _transactGet.Table(TableName, "pk", "sk");
        _transactGetTyped = _transactGet.Table<BenchOrder>(TableName);
        _suspendingWriteTable = _suspendingWrite.Table(TableName, "pk", "sk");
        _suspendingGetTable = _suspendingGet.Table(TableName, "pk", "sk");
        SetupReads();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupReads();
        _get.Dispose();
        _write.Dispose();
        _page.Dispose();
        _lastPage.Dispose();
        _count.Dispose();
        _emptyPages.Dispose();
        _emptyLastPage.Dispose();
        _counter.Dispose();
        _newItem.Dispose();
        _batchGet.Dispose();
        _transactGet.Dispose();
        _suspendingWrite.Dispose();
        _suspendingGet.Dispose();
        _getHandler.Dispose();
        _writeHandler.Dispose();
        _pageHandler.Dispose();
        _lastPageHandler.Dispose();
        _countHandler.Dispose();
        _emptyPagesHandler.Dispose();
        _emptyLastPageHandler.Dispose();
        _counterHandler.Dispose();
        _newItemHandler.Dispose();
        _batchGetHandler.Dispose();
        _transactGetHandler.Dispose();
        _suspendingWriteHandler.Dispose();
        _suspendingGetHandler.Dispose();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PutItem")]
    public async Task Put_Direct() => await Direct(_writeTable.Put());

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public async Task Put_Typed() => await _writeTyped.Put(_order);

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public async Task Put_Item() => await _writeTable.Put(FreshItem());

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public Task<PutItemResponse> Put_RequestApi() => _write.PutItemAsync(_putRequest);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("UpdateItem")]
    public async Task Update_Interpolated() =>
        await _writeTable.Update(PartitionKey, SortKey).Add("views", 1).Set("status", "paid").If($"{Attr("status")} = {"open"}");

    [Benchmark]
    [BenchmarkCategory("UpdateItem")]
    public async Task Update_StructCondition() =>
        await _writeTable.Update(PartitionKey, SortKey).Add("views", 1).Set("status", "paid").If(Attr("version").Eq(3));

    [Benchmark]
    [BenchmarkCategory("UpdateItem")]
    public async Task Update_Typed() =>
        await _writeTyped.Update(new BenchOrder.Key(PartitionKey, SortKey)).Add(OrderId, 1).Set(Currency, "EUR").If(Currency.Eq("CHF"));

    [Benchmark]
    [BenchmarkCategory("UpdateItem")]
    public Task<UpdateItemResponse> Update_RequestApi() => _write.UpdateItemAsync(_updateRequest);

    [Benchmark]
    [BenchmarkCategory("UpdateItem")]
    public async Task<int> Update_ReturnValue() => await _counterTable.Update(PartitionKey, SortKey).Add("views", 1).MaxAttempts(1).Return(Attr<int>("views"));

    [Benchmark]
    [BenchmarkCategory("UpdateItem")]
    public async Task<bool> Update_ReturnNewTyped() => (await _newItemTyped.Update(new BenchOrder.Key(PartitionKey, SortKey)).Set(Currency, "EUR").ReturnNew()).HasItem;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DeleteItem")]
    public async Task Delete_Item() => await _writeTable.Delete(PartitionKey, SortKey);

    [Benchmark]
    [BenchmarkCategory("DeleteItem")]
    public async Task Delete_Typed() => await _writeTyped.Delete(new BenchOrder.Key(PartitionKey, SortKey));

    [Benchmark]
    [BenchmarkCategory("DeleteItem")]
    public async Task Delete_Entity() => await _writeTyped.Delete(_order);

    [Benchmark]
    [BenchmarkCategory("DeleteItem")]
    public Task<DeleteItemResponse> Delete_RequestApi() => _write.DeleteItemAsync(_deleteRequest);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GetItem")]
    public async Task<Item?> Get_Item() => (await _getTable.Get(PartitionKey, SortKey)).Item;

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public async Task<bool> Get_Typed() => (await _getTyped.Get(new BenchOrder.Key(PartitionKey, SortKey))).Found;

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public Task<GetItemResponse> Get_RequestApi() => _get.GetItemAsync(_getRequest);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Query")]
    public Task<QueryResponse> Query_RequestApi() => _lastPage.QueryAsync(_queryRequest);

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Query_Items()
    {
        var count = 0;
        await foreach (var unused in _lastPageTable.Query(PartitionKey, global::DynaSharp.SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Query_Typed()
    {
        var count = 0;
        await foreach (var unused in _lastPageTyped.Query(BenchOrder.Keys.Pk.Of(PartitionKey), global::DynaSharp.SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Query_ItemsReadThree()
    {
        var length = 0;
        await foreach (var item in _lastPageTable.Query(PartitionKey, global::DynaSharp.SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).ItemsAsync())
        {
            length += item.GetString("currency")!.Length + item.GetString("sk")!.Length + (int)item.GetNumber<decimal>("total")!.Value;
        }

        return length;
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Query_TypedReadThree()
    {
        var length = 0;
        await foreach (var order in _lastPageTyped.Query(BenchOrder.Keys.Pk.Of(PartitionKey), global::DynaSharp.SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).ItemsAsync())
        {
            length += order.Currency.Length + order.Sk.Length + (int)order.Total;
        }

        return length;
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Query_PageWithNext() => (await _pageTable.Query(PartitionKey, global::DynaSharp.SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).PageAsync()).Count;

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Query_Count() => await _countTable.Query(PartitionKey).CountAsync();

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Scan_Items()
    {
        var count = 0;
        await foreach (var unused in _lastPageTable.Scan().ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async Task<int> Scan_ParallelOneSegment()
    {
        var count = 0;
        await foreach (var unused in _lastPageTable.Scan().Parallel(1).ItemsAsync())
        {
            count++;
        }

        return count;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Paging")]
    public async Task<int> Paging_OneEmptyPage() => await Follow(_emptyLastPageTable);

    [Benchmark]
    [BenchmarkCategory("Paging")]
    public async Task<int> Paging_TenEmptyPages() => await Follow(_emptyPagesTable);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Transaction")]
    public async Task Transaction_TwoPutsItem() => await _write.TransactWrite()
        .Put(_writeTable, new Item { { "pk", "EMAIL#erin@example.com" }, { "sk", "UNIQUE" }, { "customerId", "erin" } }).IfNotExists().ReturnOldOnFailure()
        .Put(_writeTable, new Item { { "pk", "CUSTOMER#erin" }, { "sk", "PROFILE" }, { "fullName", "Erin Early" }, { "email", "erin@example.com" } }).IfNotExists();

    [Benchmark]
    [BenchmarkCategory("Transaction")]
    public async Task Transaction_TwoPutsTyped() =>
        await _write.TransactWrite().Put(_writeTyped, _order).IfNotExists().ReturnOldOnFailure().Put(_writeTyped, _order).IfNotExists();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Batch")]
    public async Task Batch25_Item() => await BatchOfItems(25);

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch25_Typed() => await BatchOfOrders(25);

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch60_Item() => await BatchOfItems(60);

    [Benchmark]
    [BenchmarkCategory("Batch")]
    public async Task Batch60_Typed() => await BatchOfOrders(60);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("BatchGet")]
    public async Task<int> BatchGet25_Item()
    {
        var command = _batchGetTable.BatchGet();
        for (var i = 0; i < BatchGetKeys; i++)
        {
            command = command.Get("BATCH", BatchKeys[i]);
        }

        return (await command).Items.Count;
    }

    [Benchmark]
    [BenchmarkCategory("BatchGet")]
    public async Task<int> BatchGet25_Typed()
    {
        var command = _batchGetTyped.BatchGet();
        for (var i = 0; i < BatchGetKeys; i++)
        {
            command = command.Get(new BenchOrder.Key("BATCH", BatchKeys[i]));
        }

        return (await command).Items.Count;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("TransactGet")]
    public async Task<bool> TransactGet_TwoItems()
    {
        var result = await _transactGet.TransactGet()
            .Get(_transactGetTable, PartitionKey, SortKey, out var first)
            .Get(_transactGetTable, PartitionKey, "ORDER#2", out var second);
        return result.Read(first).Found && result.Read(second).Found;
    }

    [Benchmark]
    [BenchmarkCategory("TransactGet")]
    public async Task<bool> TransactGet_TwoTyped()
    {
        var result = await _transactGet.TransactGet()
            .Get(_transactGetTyped, new BenchOrder.Key(PartitionKey, SortKey), out var first)
            .Get(_transactGetTyped, new BenchOrder.Key(PartitionKey, "ORDER#2"), out var second);
        return result.Read(first).Found && result.Read(second).Found;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Suspending")]
    public ValueTask<WriteResult<Item>> Suspending_PutDirect() => Direct(_suspendingWriteTable.Put()).SendAsync();

    [Benchmark]
    [BenchmarkCategory("Suspending")]
    public ValueTask<GetResult<Item>> Suspending_GetItem() => _suspendingGetTable.Get(PartitionKey, "PROFILE").SendAsync();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Body")]
    public int Body_Update() =>
        Finish(_writeTable.Update(PartitionKey, SortKey).Add("views", 1).Set("status", "paid").If($"{Attr("status")} = {"open"}"));

    [Benchmark]
    [BenchmarkCategory("Body")]
    public int Body_UpdateStructCondition() =>
        Finish(_writeTable.Update(PartitionKey, SortKey).Add("views", 1).Set("status", "paid").If(Attr("status").Eq("open")));

    [Benchmark]
    [BenchmarkCategory("Body")]
    public int Body_UpdateTyped() =>
        Finish(_writeTyped.Update(new BenchOrder.Key(PartitionKey, SortKey)).Add(OrderId, 1).Set(Currency, "EUR").If(Currency.Eq("CHF")));

    [Benchmark]
    [BenchmarkCategory("Body")]
    public int Body_PutDirect() => Finish(Direct(_writeTable.Put()));

    [Benchmark]
    [BenchmarkCategory("Body")]
    public int Body_Get() => Finish(_getTable.Get(PartitionKey, SortKey).Handle);

    [Benchmark]
    [BenchmarkCategory("Body")]
    public int Body_Query() =>
        Finish(_getTable.Query(PartitionKey, global::DynaSharp.SortKeyCondition.BeginsWith("ORDER#")).Filter(Attr("status").Eq("open")).Limit(25).Handle);

    private static string QueryPage(string end)
    {
        var page = new StringBuilder("{\"Items\":[");
        for (var i = 0; i < QueryPageSize; i++)
        {
            page.Append(i == 0 ? string.Empty : ",").Append(ItemAttributes);
        }

        var size = QueryPageSize.ToString(CultureInfo.InvariantCulture);
        return page.Append("],\"Count\":").Append(size).Append(",\"ScannedCount\":").Append(size).Append(end).Append('}').ToString();
    }

    private static string BatchGetResponse()
    {
        var response = new StringBuilder("{\"Responses\":{\"" + TableName + "\":[");
        for (var i = 0; i < BatchGetKeys; i++)
        {
            response.Append(i == 0 ? string.Empty : ",").Append(ItemAttributes);
        }

        return response.Append("]},\"UnprocessedKeys\":{}}").ToString();
    }

    private static async Task<int> Follow(Table<Item> table)
    {
        var count = 0;
        await foreach (var unused in table.Query(PartitionKey).ItemsAsync())
        {
            count++;
        }

        return count;
    }

    private static PutCommand<Item> Direct(PutCommand<Item> command) => command
        .With("pk", PartitionKey)
        .With("sk", SortKey)
        .With("type", "Order")
        .With("orderId", 9182736455)
        .With("total", 1249.95m)
        .With("currency", "CHF")
        .With("shipped", true)
        .With("giftNote", AttributeValue.Null);

    private static Item FreshItem() => new()
    {
        ["pk"] = PartitionKey,
        ["sk"] = SortKey,
        ["type"] = "Order",
        ["orderId"] = 9182736455,
        ["total"] = 1249.95m,
        ["currency"] = "CHF",
        ["shipped"] = true,
        ["giftNote"] = AttributeValue.Null,
    };

    // What the pipeline does with a command at the start and the end of a call, without the call.
    private static int Finish(CommandHandle command)
    {
        var state = CommandState.Enter(command.State, command.Version, command.Operation, command.Table);
        var version = state.Version;
        var length = state.Finish().Count;
        state.Release(version);
        return length;
    }

    private static DynamoDbClient Create(HttpMessageHandler handler) => new(new DynamoDbClientOptions
    {
        Region = AwsRegion.FromName("eu-central-2"),
        Credentials = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")),
        HttpHandler = handler,
    });

    private BatchWriteCommand<Item> BatchOfItems(int count)
    {
        var command = _writeTable.BatchWrite();
        for (var i = 0; i < count; i++)
        {
            command = command.Put(new Item(3) { { "pk", "BATCH" }, { "sk", BatchKeys[i] }, { "n", i } });
        }

        return command;
    }

    private BatchWriteCommand<BenchOrder> BatchOfOrders(int count)
    {
        var command = _writeTyped.BatchWrite();
        for (var i = 0; i < count; i++)
        {
            command = command.Put(_order);
        }

        return command;
    }
}
