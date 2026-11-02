using System.Diagnostics.CodeAnalysis;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using EfficientDynamoDb;
using static DynaSharp.Benchmarks.ClientComparisonBenchmarks;

namespace DynaSharp.Benchmarks;

/// <summary>
/// Generated entities against hand-written mapping, and against the mapped APIs of EfficientDynamoDb and the AWS SDK, on the canned HTTP responses of
/// <see cref="ClientComparisonBenchmarks"/>. A call builds what it needs, as a caller does; the entity exists already. The GetItem, PutItem and Query100
/// groups are whole calls. The Read and Write groups are one item without a client, which is the "per item" row of generator design section 6.
/// The setup checks that each client sent the right operation and parsed the response.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet needs an unsealed type and owns the lifetime through GlobalCleanup.")]
public class EntityBenchmarks
{
    private const string OrderSortKey = ClientComparisonBenchmarks.SortKey;

    private const string SortKeyWithTicks = "ORDER#2026-10-31T12:34:56.7890000Z";

    private static readonly Guid UserId = new("4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19");

    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 31, 12, 34, 56, 789, TimeSpan.Zero);

    private static readonly byte[] ItemJson = Encoding.UTF8.GetBytes(ItemAttributes);

    private static readonly byte[] ItemWithTicksJson = Encoding.UTF8.GetBytes(ItemAttributes.Replace(OrderSortKey, SortKeyWithTicks, StringComparison.Ordinal));

    private static readonly EntityAddress Address = new("Bahnhofstrasse 1", "Zürich", "8001");

    private readonly EntityOrder _order = new()
    {
        Pk = PartitionKey,
        Sk = OrderSortKey,
        OrderId = 9182736455,
        Total = 1249.95m,
        Currency = "CHF",
        Shipped = true,
        Tags = ["priority", "fragile", "insured"],
        Address = Address,
    };

    private readonly EntityOrderWithSet _orderWithSet = new()
    {
        Pk = PartitionKey,
        Sk = OrderSortKey,
        OrderId = 9182736455,
        Total = 1249.95m,
        Currency = "CHF",
        Shipped = true,
        Tags = ["priority", "fragile", "insured"],
        Address = Address,
    };

    private readonly EntityOrderWithTemplateKeys _orderWithTemplateKeys = new()
    {
        UserId = UserId,
        CreatedAt = CreatedAt,
        OrderId = 9182736455,
        Total = 1249.95m,
        Currency = "CHF",
        Shipped = true,
        Tags = ["priority", "fragile", "insured"],
        Address = Address,
    };

    private readonly HandWrittenOrder _handWritten = new()
    {
        Pk = PartitionKey,
        Sk = OrderSortKey,
        Type = "Order",
        OrderId = 9182736455,
        Total = 1249.95m,
        Currency = "CHF",
        Shipped = true,
        Tags = ["priority", "fragile", "insured"],
        Address = new HandWrittenAddress("Bahnhofstrasse 1", "Zürich", "8001"),
    };

    private readonly EfficientDynamoDbOrder _efficientDynamoDbOrder = new()
    {
        Pk = PartitionKey,
        Sk = OrderSortKey,
        Type = "Order",
        OrderId = 9182736455,
        Total = 1249.95m,
        Currency = "CHF",
        Shipped = true,
        Tags = ["priority", "fragile", "insured"],
        Address = new EfficientDynamoDbAddress { Street = "Bahnhofstrasse 1", City = "Zürich", Zip = "8001" },
    };

    private readonly AwsSdkOrder _awsSdkOrder = new()
    {
        Pk = PartitionKey,
        Sk = OrderSortKey,
        Type = "Order",
        OrderId = 9182736455,
        Total = 1249.95m,
        Currency = "CHF",
        Shipped = true,
        Tags = ["priority", "fragile", "insured"],
        Address = new AwsSdkAddress { Street = "Bahnhofstrasse 1", City = "Zürich", Zip = "8001" },
    };

    private CannedHandler _getHandler = null!;
    private CannedHandler _getWithTicksHandler = null!;
    private CannedHandler _putHandler = null!;
    private CannedHandler _queryHandler = null!;
    private CannedHandler _queryWithTicksHandler = null!;

    private DynamoDbClient _get = null!;
    private DynamoDbClient _getWithTicks = null!;
    private DynamoDbClient _put = null!;
    private DynamoDbClient _query = null!;
    private DynamoDbClient _queryWithTicks = null!;

    private Table<EntityOrder> _getOrders = null!;
    private Table<EntityOrderWithSet> _getOrdersWithSet = null!;
    private Table<EntityOrderWithTemplateKeys> _getOrdersWithTemplateKeys = null!;
    private Table<HandWrittenOrder> _getHandWritten = null!;
    private Table<Item> _getItems = null!;
    private Table<EntityOrder> _putOrders = null!;
    private Table<EntityOrderWithSet> _putOrdersWithSet = null!;
    private Table<EntityOrderWithTemplateKeys> _putOrdersWithTemplateKeys = null!;
    private Table<HandWrittenOrder> _putHandWritten = null!;
    private Table<EntityOrder> _queryOrders = null!;
    private Table<EntityOrderWithSet> _queryOrdersWithSet = null!;
    private Table<EntityOrderWithTemplateKeys> _queryOrdersWithTemplateKeys = null!;
    private Table<HandWrittenOrder> _queryHandWritten = null!;
    private Table<Item> _queryItems = null!;

    private DynamoDbContext _efficientGet = null!;
    private DynamoDbContext _efficientPut = null!;
    private DynamoDbContext _efficientQuery = null!;

    private AmazonDynamoDBClient _awsGetClient = null!;
    private AmazonDynamoDBClient _awsPutClient = null!;
    private AmazonDynamoDBClient _awsQueryClient = null!;
    private DynamoDBContext _awsGet = null!;
    private DynamoDBContext _awsPut = null!;
    private DynamoDBContext _awsQuery = null!;

    private Dictionary<string, AttributeValue> _dictionary = null!;
    private GetItemRequest _getRequest = null!;
    private PutItemRequest _putRequest = null!;
    private QueryRequest _queryRequest = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var withTicks = ItemAttributes.Replace(OrderSortKey, SortKeyWithTicks, StringComparison.Ordinal);
        _getHandler = new CannedHandler("{\"Item\":" + ItemAttributes + "}");
        _getWithTicksHandler = new CannedHandler("{\"Item\":" + withTicks + "}");
        _putHandler = new CannedHandler("{}");
        _queryHandler = new CannedHandler(CreateQueryPage());
        _queryWithTicksHandler = new CannedHandler(CreateQueryPage().Replace(OrderSortKey, SortKeyWithTicks, StringComparison.Ordinal));

        _get = CreateDynaSharp(_getHandler);
        _getWithTicks = CreateDynaSharp(_getWithTicksHandler);
        _put = CreateDynaSharp(_putHandler);
        _query = CreateDynaSharp(_queryHandler);
        _queryWithTicks = CreateDynaSharp(_queryWithTicksHandler);

        _getOrders = _get.Table<EntityOrder>(TableName);
        _getOrdersWithSet = _get.Table<EntityOrderWithSet>(TableName);
        _getOrdersWithTemplateKeys = _getWithTicks.Table<EntityOrderWithTemplateKeys>(TableName);
        _getHandWritten = _get.Table<HandWrittenOrder>(TableName);
        _getItems = _get.Table(TableName, "pk", "sk");
        _putOrders = _put.Table<EntityOrder>(TableName);
        _putOrdersWithSet = _put.Table<EntityOrderWithSet>(TableName);
        _putOrdersWithTemplateKeys = _put.Table<EntityOrderWithTemplateKeys>(TableName);
        _putHandWritten = _put.Table<HandWrittenOrder>(TableName);
        _queryOrders = _query.Table<EntityOrder>(TableName);
        _queryOrdersWithSet = _query.Table<EntityOrderWithSet>(TableName);
        _queryOrdersWithTemplateKeys = _queryWithTicks.Table<EntityOrderWithTemplateKeys>(TableName);
        _queryHandWritten = _query.Table<HandWrittenOrder>(TableName);
        _queryItems = _query.Table(TableName, "pk", "sk");

        _efficientGet = CreateEfficientDynamoDb(_getHandler);
        _efficientPut = CreateEfficientDynamoDb(_putHandler);
        _efficientQuery = CreateEfficientDynamoDb(_queryHandler);

        _awsGetClient = CreateAwsSdk(_getHandler);
        _awsPutClient = CreateAwsSdk(_putHandler);
        _awsQueryClient = CreateAwsSdk(_queryHandler);
        _awsGet = CreateAwsContext(_awsGetClient);
        _awsPut = CreateAwsContext(_awsPutClient);
        _awsQuery = CreateAwsContext(_awsQueryClient);

        _getRequest = CreateDynaSharpGetRequest();
        _putRequest = CreateDynaSharpPutRequest();
        _dictionary = _putRequest.Item.ToDictionary();
        _queryRequest = new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = PartitionKey, [":prefix"] = "ORDER#" },
            Limit = QueryPageSize,
        };

        await VerifyAsync();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _get.Dispose();
        _getWithTicks.Dispose();
        _put.Dispose();
        _query.Dispose();
        _queryWithTicks.Dispose();
        _awsGet.Dispose();
        _awsPut.Dispose();
        _awsQuery.Dispose();
        _awsGetClient.Dispose();
        _awsPutClient.Dispose();
        _awsQueryClient.Dispose();
        _getHandler.Dispose();
        _getWithTicksHandler.Dispose();
        _putHandler.Dispose();
        _queryHandler.Dispose();
        _queryWithTicksHandler.Dispose();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GetItem")]
    public async Task<string?> Get_Generated() => (await _getOrders.Get(EntityOrder.Keys.Of(PartitionKey, OrderSortKey)).SendAsync()).Item?.Currency;

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public async Task<string?> Get_GeneratedWithSet() => (await _getOrdersWithSet.Get(EntityOrderWithSet.Keys.Of(PartitionKey, OrderSortKey)).SendAsync()).Item?.Currency;

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public async Task<string?> Get_GeneratedTemplateKeys() =>
        (await _getOrdersWithTemplateKeys.Get(EntityOrderWithTemplateKeys.Keys.Of(UserId, CreatedAt)).SendAsync()).Item?.Currency;

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public async Task<string?> Get_HandWritten() => (await _getHandWritten.Get(new HandWrittenOrder.Key(PartitionKey, OrderSortKey)).SendAsync()).Item?.Currency;

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public async Task<string?> Get_Item() => (await _getItems.Get(PartitionKey, OrderSortKey).SendAsync()).Item?.GetString("currency");

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public Task<GetItemResponse> Get_RequestApi() => _get.GetItemAsync(_getRequest);

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public async Task<string> Get_EfficientDynamoDb() => (await _efficientGet.GetItemAsync<EfficientDynamoDbOrder>(PartitionKey, OrderSortKey))!.Currency;

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public async Task<string> Get_AwsSdk() => (await _awsGet.LoadAsync<AwsSdkOrder>(PartitionKey, OrderSortKey)).Currency;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PutItem")]
    public async Task Put_Generated() => await _putOrders.Put(_order).SendAsync();

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public async Task Put_GeneratedWithSet() => await _putOrdersWithSet.Put(_orderWithSet).SendAsync();

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public async Task Put_GeneratedTemplateKeys() => await _putOrdersWithTemplateKeys.Put(_orderWithTemplateKeys).SendAsync();

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public async Task Put_HandWritten() => await _putHandWritten.Put(_handWritten).SendAsync();

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public Task<PutItemResponse> Put_RequestApi() => _put.PutItemAsync(_putRequest);

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public Task Put_EfficientDynamoDb() => _efficientPut.PutItemAsync(_efficientDynamoDbOrder);

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public Task Put_AwsSdk() => _awsPut.SaveAsync(_awsSdkOrder);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_Generated() =>
        (await _queryOrders.Query(PartitionKey, SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).PageAsync()).Items.Count;

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_GeneratedWithSet() =>
        (await _queryOrdersWithSet.Query(PartitionKey, SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).PageAsync()).Items.Count;

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_GeneratedTemplateKeys() =>
        (await _queryOrdersWithTemplateKeys
            .Query(EntityOrderWithTemplateKeys.Keys.Pk.Format(UserId), SortKeyCondition.BeginsWith(EntityOrderWithTemplateKeys.Keys.Sk.Prefix()))
            .Limit(QueryPageSize)
            .PageAsync()).Items.Count;

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_HandWritten() =>
        (await _queryHandWritten.Query(PartitionKey, SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).PageAsync()).Items.Count;

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_Item() =>
        (await _queryItems.Query(PartitionKey, SortKeyCondition.BeginsWith("ORDER#")).Limit(QueryPageSize).PageAsync()).Items.Count;

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_RequestApi() => (await _query.QueryAsync(_queryRequest)).Items.Count;

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_EfficientDynamoDb() =>
        (await _efficientQuery.Query<EfficientDynamoDbOrder>()
            .WithKeyExpression(Joiner.And(Condition<EfficientDynamoDbOrder>.On(order => order.Pk).EqualTo(PartitionKey), Condition<EfficientDynamoDbOrder>.On(order => order.Sk).BeginsWith("ORDER#")))
            .WithLimit(QueryPageSize)
            .ToPageAsync()).Items.Count;

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public async Task<int> Query_AwsSdk() => (await _awsQuery.QueryAsync<AwsSdkOrder>(PartitionKey, QueryOperator.BeginsWith, ["ORDER#"]).GetNextSetAsync()).Count;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Read")]
    public string Read_Generated() => Read<EntityOrder>(ItemJson).Currency;

    [Benchmark]
    [BenchmarkCategory("Read")]
    public string Read_GeneratedWithSet() => Read<EntityOrderWithSet>(ItemJson).Currency;

    [Benchmark]
    [BenchmarkCategory("Read")]
    public string Read_GeneratedTemplateKeys() => Read<EntityOrderWithTemplateKeys>(ItemWithTicksJson).Currency;

    [Benchmark]
    [BenchmarkCategory("Read")]
    public string Read_HandWritten() => Read<HandWrittenOrder>(ItemJson).Currency;

    [Benchmark]
    [BenchmarkCategory("Read")]
    public int Read_Item()
    {
        var reader = new DynamoDbReader(ItemJson);
        return AttributeValueConverter.ReadItem(ref reader).Count;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Write")]
    public int Write_Generated() => Write(_order);

    [Benchmark]
    [BenchmarkCategory("Write")]
    public int Write_GeneratedWithSet() => Write(_orderWithSet);

    [Benchmark]
    [BenchmarkCategory("Write")]
    public int Write_GeneratedTemplateKeys() => Write(_orderWithTemplateKeys);

    [Benchmark]
    [BenchmarkCategory("Write")]
    public int Write_HandWritten() => Write(_handWritten);

    [Benchmark]
    [BenchmarkCategory("Write")]
    public int Write_Dictionary()
    {
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            AttributeValueConverter.WriteItem(new DynamoDbWriter(json), _dictionary);
        }

        return buffer.WrittenCount;
    }

    private static T Read<T>(byte[] utf8Json)
        where T : IDynamoDbDocument<T>
    {
        var reader = new DynamoDbReader(utf8Json);
        reader.ReadStartItem();
        return T.ReadAttributes(ref reader);
    }

    private static int Write<T>(T entity)
        where T : IDynamoDbDocument<T>
    {
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            var writer = new DynamoDbWriter(json);
            writer.WriteStartItem();
            T.WriteAttributes(writer, entity);
            writer.WriteEndItem();
        }

        return buffer.WrittenCount;
    }

    // The attributes above are all the context needs, so it must not ask for the table first, and V2 is what stores a bool as a bool.
    private static DynamoDBContext CreateAwsContext(AmazonDynamoDBClient client) =>
        new DynamoDBContextBuilder()
            .WithDynamoDBClient(() => client)
            .ConfigureContext(config =>
            {
                config.DisableFetchingTableMetadata = true;
                config.Conversion = DynamoDBEntryConversion.V2;
            })
            .Build();

    private async Task VerifyAsync()
    {
        _getHandler.Capture = _putHandler.Capture = _queryHandler.Capture = true;
        try
        {
            Check(await Get_Generated() == "CHF" && await Get_GeneratedWithSet() == "CHF" && await Get_GeneratedTemplateKeys() == "CHF" && await Get_HandWritten() == "CHF" && await Get_Item() == "CHF", "a DynaSharp Get");
            Check(await Get_EfficientDynamoDb() == "CHF", "EfficientDynamoDb Get");
            CheckRequest(_getHandler, "GetItem", "EfficientDynamoDb");
            Check(await Get_AwsSdk() == "CHF", "AWS SDK Get");
            CheckRequest(_getHandler, "GetItem", "AWS SDK");

            await Put_Generated();
            CheckRequest(_putHandler, "PutItem", "DynaSharp");
            await Put_EfficientDynamoDb();
            CheckRequest(_putHandler, "PutItem", "EfficientDynamoDb");
            await Put_AwsSdk();
            CheckRequest(_putHandler, "UpdateItem", "AWS SDK");

            Check(await Query_Generated() == QueryPageSize && await Query_GeneratedWithSet() == QueryPageSize && await Query_GeneratedTemplateKeys() == QueryPageSize && await Query_HandWritten() == QueryPageSize && await Query_Item() == QueryPageSize, "a DynaSharp Query");
            Check(await Query_EfficientDynamoDb() == QueryPageSize, "EfficientDynamoDb Query");
            CheckRequest(_queryHandler, "Query", "EfficientDynamoDb");
            Check(await Query_AwsSdk() == QueryPageSize, "AWS SDK Query");
            CheckRequest(_queryHandler, "Query", "AWS SDK");
        }
        finally
        {
            _getHandler.Capture = _putHandler.Capture = _queryHandler.Capture = false;
        }

        // What Get and Query do on the other clients comes back whole, so that a lazy or partial read cannot look cheap.
        var efficient = (await _efficientGet.GetItemAsync<EfficientDynamoDbOrder>(PartitionKey, OrderSortKey))!;
        var aws = await _awsGet.LoadAsync<AwsSdkOrder>(PartitionKey, OrderSortKey);
        var efficientPage = await _efficientQuery.Query<EfficientDynamoDbOrder>()
            .WithKeyExpression(Joiner.And(Condition<EfficientDynamoDbOrder>.On(order => order.Pk).EqualTo(PartitionKey), Condition<EfficientDynamoDbOrder>.On(order => order.Sk).BeginsWith("ORDER#")))
            .ToPageAsync();
        var awsPage = await _awsQuery.QueryAsync<AwsSdkOrder>(PartitionKey, QueryOperator.BeginsWith, ["ORDER#"]).GetNextSetAsync();
        Check(efficient.Address.City == "Zürich" && efficient.Tags.Count == 3 && efficient.Total == 1249.95m && efficient.Shipped, "the EfficientDynamoDb entity");
        Check(aws.Address.City == "Zürich" && aws.Tags.Count == 3 && aws.Total == 1249.95m && aws.Shipped, "the AWS SDK entity");
        Check(efficientPage.Items[^1].Address.Zip == "8001" && awsPage[^1].Address.Zip == "8001", "the pages of the other clients");

        var generated = Read<EntityOrder>(ItemJson);
        var templated = Read<EntityOrderWithTemplateKeys>(ItemWithTicksJson);
        Check(generated.Address.City == "Zürich" && generated.Tags.Length == 3 && generated.Sk == OrderSortKey, "the generated read");
        Check(templated.UserId == UserId && templated.CreatedAt == CreatedAt, "the keys of the template read");
        Check(Write(_order) > 300 && Write(_orderWithTemplateKeys) > 300, "the generated write");
    }
}
