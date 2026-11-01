using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using EfficientDynamoDb;
using EfficientDynamoDb.Configs;
using EfficientDynamoDb.DocumentModel;
using Aws = Amazon.DynamoDBv2.Model;
using Dyna = DynaSharp.Requests;
using Edb = EfficientDynamoDb.Operations;

namespace DynaSharp.Benchmarks;

/// <summary>
/// DynaSharp, EfficientDynamoDb and the AWS SDK for .NET answering the same canned HTTP responses: the same item, the same page
/// of 100 items, the same stub handler behind each client's own handler seam. Each client is used through its low-level API
/// (typed requests, dictionaries or documents of attribute values, no object mapping), the layer all three share. The setup
/// checks that every client sent the right operation and parsed the response, so a benchmark cannot pass while doing nothing.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet needs an unsealed type and owns the lifetime through GlobalCleanup.")]
public class ClientComparisonBenchmarks
{
    internal const string TableName = "Orders";
    internal const string PartitionKey = "USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19";
    internal const string SortKey = "ORDER#2026-10-31T12:34:56.789Z";

    // The attributes of an order: ten attributes of the common types, about 400 bytes as DynamoDB sends them.
    internal const string ItemAttributes = """
        {"pk":{"S":"USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19"},"sk":{"S":"ORDER#2026-10-31T12:34:56.789Z"},"type":{"S":"Order"},
        "orderId":{"N":"9182736455"},"total":{"N":"1249.95"},"currency":{"S":"CHF"},"shipped":{"BOOL":true},"giftNote":{"NULL":true},
        "tags":{"SS":["priority","fragile","insured"]},"address":{"M":{"street":{"S":"Bahnhofstrasse 1"},"city":{"S":"Zürich"},"zip":{"S":"8001"}}}}
        """;

    internal const string AccessKey = "AKIAIOSFODNN7EXAMPLE";
    internal const string SecretKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

    internal const int QueryPageSize = 100;

    private CannedHandler _getHandler = null!;
    private CannedHandler _putHandler = null!;
    private CannedHandler _queryHandler = null!;

    private DynamoDbClient _dynaGet = null!;
    private DynamoDbClient _dynaPut = null!;
    private DynamoDbClient _dynaQuery = null!;
    private DynamoDbContext _edbGet = null!;
    private DynamoDbContext _edbPut = null!;
    private DynamoDbContext _edbQuery = null!;
    private AmazonDynamoDBClient _awsGet = null!;
    private AmazonDynamoDBClient _awsPut = null!;
    private AmazonDynamoDBClient _awsQuery = null!;

    private Dyna.GetItemRequest _dynaGetRequest = null!;
    private Dyna.PutItemRequest _dynaPutRequest = null!;
    private Dyna.QueryRequest _dynaQueryRequest = null!;
    private Edb.GetItem.GetItemRequest _edbGetRequest = null!;
    private Edb.PutItem.PutItemRequest _edbPutRequest = null!;
    private Edb.Query.QueryRequest _edbQueryRequest = null!;
    private Aws.GetItemRequest _awsGetRequest = null!;
    private Aws.PutItemRequest _awsPutRequest = null!;
    private Aws.QueryRequest _awsQueryRequest = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _getHandler = new CannedHandler("{\"Item\":" + ItemAttributes + "}");
        _putHandler = new CannedHandler("{}");
        _queryHandler = new CannedHandler(CreateQueryPage());

        _dynaGet = CreateDynaSharp(_getHandler);
        _dynaPut = CreateDynaSharp(_putHandler);
        _dynaQuery = CreateDynaSharp(_queryHandler);
        _edbGet = CreateEfficientDynamoDb(_getHandler);
        _edbPut = CreateEfficientDynamoDb(_putHandler);
        _edbQuery = CreateEfficientDynamoDb(_queryHandler);
        _awsGet = CreateAwsSdk(_getHandler);
        _awsPut = CreateAwsSdk(_putHandler);
        _awsQuery = CreateAwsSdk(_queryHandler);

        CreateDynaSharpRequests();
        CreateEfficientDynamoDbRequests();
        CreateAwsSdkRequests();

        await VerifyGetItemAsync();
        await VerifyPutItemAsync();
        await VerifyQueryAsync();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dynaGet.Dispose();
        _dynaPut.Dispose();
        _dynaQuery.Dispose();
        _awsGet.Dispose();
        _awsPut.Dispose();
        _awsQuery.Dispose();
        _getHandler.Dispose();
        _putHandler.Dispose();
        _queryHandler.Dispose();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("GetItem")]
    public Task<Dyna.GetItemResponse> GetItem_DynaSharp() => _dynaGet.GetItemAsync(_dynaGetRequest);

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public Task<Edb.GetItem.GetItemResponse> GetItem_EfficientDynamoDb() => _edbGet.LowLevel.GetItemAsync(_edbGetRequest);

    [Benchmark]
    [BenchmarkCategory("GetItem")]
    public Task<Aws.GetItemResponse> GetItem_AwsSdk() => _awsGet.GetItemAsync(_awsGetRequest);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PutItem")]
    public Task<Dyna.PutItemResponse> PutItem_DynaSharp() => _dynaPut.PutItemAsync(_dynaPutRequest);

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public Task<Edb.PutItem.PutItemResponse> PutItem_EfficientDynamoDb() => _edbPut.LowLevel.PutItemAsync(_edbPutRequest);

    [Benchmark]
    [BenchmarkCategory("PutItem")]
    public Task<Aws.PutItemResponse> PutItem_AwsSdk() => _awsPut.PutItemAsync(_awsPutRequest);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Query100")]
    public Task<Dyna.QueryResponse> Query100_DynaSharp() => _dynaQuery.QueryAsync(_dynaQueryRequest);

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public Task<Edb.Query.QueryResponse> Query100_EfficientDynamoDb() => _edbQuery.LowLevel.QueryAsync(_edbQueryRequest);

    [Benchmark]
    [BenchmarkCategory("Query100")]
    public Task<Aws.QueryResponse> Query100_AwsSdk() => _awsQuery.QueryAsync(_awsQueryRequest);

    internal static Dyna.GetItemRequest CreateDynaSharpGetRequest() => new()
    {
        TableName = TableName,
        Key = new Dictionary<string, DynaSharp.AttributeValue> { ["pk"] = PartitionKey, ["sk"] = SortKey },
    };

    internal static Dyna.PutItemRequest CreateDynaSharpPutRequest() =>
        new()
        {
            TableName = TableName,
            Item = new Dictionary<string, DynaSharp.AttributeValue>
            {
                ["pk"] = PartitionKey,
                ["sk"] = SortKey,
                ["type"] = "Order",
                ["orderId"] = DynaSharp.AttributeValue.FromNumber("9182736455"),
                ["total"] = DynaSharp.AttributeValue.FromNumber("1249.95"),
                ["currency"] = "CHF",
                ["shipped"] = DynaSharp.AttributeValue.FromBool(true),
                ["giftNote"] = DynaSharp.AttributeValue.Null,
                ["tags"] = DynaSharp.AttributeValue.FromStringSet(["priority", "fragile", "insured"]),
                ["address"] = DynaSharp.AttributeValue.FromMap(
                [
                    new("street", "Bahnhofstrasse 1"),
                    new("city", "Zürich"),
                    new("zip", "8001"),
                ]),
            },
        };

    internal static DynamoDbClient CreateDynaSharp(HttpMessageHandler handler) => new(new DynamoDbClientOptions
    {
        Region = AwsRegion.FromName("eu-central-2"),
        Credentials = new StaticCredentialsProvider(new DynaSharp.Credentials.AwsCredentials(AccessKey, SecretKey)),
        HttpHandler = handler,
    });

    internal static DynamoDbContext CreateEfficientDynamoDb(HttpMessageHandler handler) => new(
        new DynamoDbContextConfig(RegionEndpoint.Create("eu-central-2"), new EfficientDynamoDb.Configs.AwsCredentials(AccessKey, SecretKey))
        {
            HttpClientFactory = new EfficientDynamoDbHttpClientFactory(handler),
        });

    internal static AmazonDynamoDBClient CreateAwsSdk(HttpMessageHandler handler) => new(
        new BasicAWSCredentials(AccessKey, SecretKey),
        new AmazonDynamoDBConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.EUCentral2,
            HttpClientFactory = new AwsSdkHttpClientFactory(handler),
        });

    internal static string CreateQueryPage()
    {
        var page = new StringBuilder("{\"Items\":[");
        for (var i = 0; i < QueryPageSize; i++)
        {
            page.Append(i == 0 ? string.Empty : ",").Append(ItemAttributes);
        }

        return page.Append("],\"Count\":").Append(QueryPageSize.ToString(CultureInfo.InvariantCulture))
            .Append(",\"ScannedCount\":").Append(QueryPageSize.ToString(CultureInfo.InvariantCulture))
            .Append(",\"LastEvaluatedKey\":{\"pk\":{\"S\":\"").Append(PartitionKey).Append("\"},\"sk\":{\"S\":\"").Append(SortKey).Append("\"}}}")
            .ToString();
    }

    internal static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException("The benchmark does not do the work it should: " + description);
        }
    }

    internal static void CheckRequest(CannedHandler handler, string operation, string client)
    {
        Check(handler.LastTarget == "DynamoDB_20120810." + operation, client + " sent " + handler.LastTarget);
        Check(handler.LastBody is { } body && body.Contains($"\"{TableName}\"", StringComparison.Ordinal) && body.Contains(PartitionKey, StringComparison.Ordinal), client + " sent a body without table or key");
    }

    private static EfficientDynamoDb.DocumentModel.AttributeValue EdbString(string value) => new(new StringAttributeValue(value));

    private void CreateDynaSharpRequests()
    {
        _dynaGetRequest = CreateDynaSharpGetRequest();
        _dynaPutRequest = CreateDynaSharpPutRequest();
        _dynaQueryRequest = new Dyna.QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefix)",
            ExpressionAttributeValues = new Dictionary<string, DynaSharp.AttributeValue> { [":pk"] = PartitionKey, [":prefix"] = "ORDER#" },
            Limit = QueryPageSize,
        };
    }

    private void CreateEfficientDynamoDbRequests()
    {
        _edbGetRequest = new Edb.GetItem.GetItemRequest
        {
            TableName = TableName,
            Key = new Edb.Shared.PrimaryKey("pk", EdbString(PartitionKey), "sk", EdbString(SortKey)),
        };
        _edbPutRequest = new Edb.PutItem.PutItemRequest
        {
            TableName = TableName,
            Item = new Document
            {
                ["pk"] = EdbString(PartitionKey),
                ["sk"] = EdbString(SortKey),
                ["type"] = EdbString("Order"),
                ["orderId"] = new EfficientDynamoDb.DocumentModel.AttributeValue(new NumberAttributeValue("9182736455")),
                ["total"] = new EfficientDynamoDb.DocumentModel.AttributeValue(new NumberAttributeValue("1249.95")),
                ["currency"] = EdbString("CHF"),
                ["shipped"] = new EfficientDynamoDb.DocumentModel.AttributeValue(new BoolAttributeValue(true)),
                ["giftNote"] = new EfficientDynamoDb.DocumentModel.AttributeValue(new NullAttributeValue(true)),
                ["tags"] = new EfficientDynamoDb.DocumentModel.AttributeValue(new StringSetAttributeValue(["priority", "fragile", "insured"])),
                ["address"] = new EfficientDynamoDb.DocumentModel.AttributeValue(new MapAttributeValue(new Document
                {
                    ["street"] = EdbString("Bahnhofstrasse 1"),
                    ["city"] = EdbString("Zürich"),
                    ["zip"] = EdbString("8001"),
                })),
            },
        };
        _edbQueryRequest = new Edb.Query.QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefix)",
            ExpressionAttributeValues = new Dictionary<string, EfficientDynamoDb.DocumentModel.AttributeValue> { [":pk"] = EdbString(PartitionKey), [":prefix"] = EdbString("ORDER#") },
            Limit = QueryPageSize,
        };
    }

    private void CreateAwsSdkRequests()
    {
        _awsGetRequest = new Aws.GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, Aws.AttributeValue> { ["pk"] = new() { S = PartitionKey }, ["sk"] = new() { S = SortKey } },
        };
        _awsPutRequest = new Aws.PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, Aws.AttributeValue>
            {
                ["pk"] = new() { S = PartitionKey },
                ["sk"] = new() { S = SortKey },
                ["type"] = new() { S = "Order" },
                ["orderId"] = new() { N = "9182736455" },
                ["total"] = new() { N = "1249.95" },
                ["currency"] = new() { S = "CHF" },
                ["shipped"] = new() { BOOL = true },
                ["giftNote"] = new() { NULL = true },
                ["tags"] = new() { SS = ["priority", "fragile", "insured"] },
                ["address"] = new()
                {
                    M = new Dictionary<string, Aws.AttributeValue>
                    {
                        ["street"] = new() { S = "Bahnhofstrasse 1" },
                        ["city"] = new() { S = "Zürich" },
                        ["zip"] = new() { S = "8001" },
                    },
                },
            },
        };
        _awsQueryRequest = new Aws.QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefix)",
            ExpressionAttributeValues = new Dictionary<string, Aws.AttributeValue> { [":pk"] = new() { S = PartitionKey }, [":prefix"] = new() { S = "ORDER#" } },
            Limit = QueryPageSize,
        };
    }

    // The three verification methods run each benchmark once with the handler capturing: the request must name the operation, the
    // table and the key, and the response must come back parsed (values inside the item, the address, the page, the counts).
    private async Task VerifyGetItemAsync()
    {
        _getHandler.Capture = true;
        try
        {
            var dyna = await GetItem_DynaSharp();
            CheckRequest(_getHandler, "GetItem", "DynaSharp");
            var edb = await GetItem_EfficientDynamoDb();
            CheckRequest(_getHandler, "GetItem", "EfficientDynamoDb");
            var aws = await GetItem_AwsSdk();
            CheckRequest(_getHandler, "GetItem", "AWS SDK");

            Check(dyna.Item!["pk"].AsString() == PartitionKey && dyna.Item["address"].AsMap()["city"].AsString() == "Zürich", "DynaSharp GetItem");
            Check(edb.Item!["pk"].AsString() == PartitionKey && edb.Item["address"].AsMapAttribute().Value["city"].AsString() == "Zürich", "EfficientDynamoDb GetItem");
            Check(aws.Item["pk"].S == PartitionKey && aws.Item["address"].M["city"].S == "Zürich", "AWS SDK GetItem");
        }
        finally
        {
            _getHandler.Capture = false;
        }
    }

    private async Task VerifyPutItemAsync()
    {
        _putHandler.Capture = true;
        try
        {
            await PutItem_DynaSharp();
            CheckRequest(_putHandler, "PutItem", "DynaSharp");
            await PutItem_EfficientDynamoDb();
            CheckRequest(_putHandler, "PutItem", "EfficientDynamoDb");
            await PutItem_AwsSdk();
            CheckRequest(_putHandler, "PutItem", "AWS SDK");
        }
        finally
        {
            _putHandler.Capture = false;
        }
    }

    private async Task VerifyQueryAsync()
    {
        _queryHandler.Capture = true;
        try
        {
            var dyna = await Query100_DynaSharp();
            CheckRequest(_queryHandler, "Query", "DynaSharp");
            var edb = await Query100_EfficientDynamoDb();
            CheckRequest(_queryHandler, "Query", "EfficientDynamoDb");
            var aws = await Query100_AwsSdk();
            CheckRequest(_queryHandler, "Query", "AWS SDK");

            Check(dyna.Count == QueryPageSize && dyna.Items.Count == QueryPageSize && dyna.Items[^1]["sk"].AsString() == SortKey && dyna.LastEvaluatedKey is not null, "DynaSharp Query");
            Check(edb.Count == QueryPageSize && edb.Items.Count == QueryPageSize && edb.Items[^1]["sk"].AsString() == SortKey && edb.LastEvaluatedKey is not null, "EfficientDynamoDb Query");
            Check(aws.Count == QueryPageSize && aws.Items.Count == QueryPageSize && aws.Items[^1]["sk"].S == SortKey && aws.LastEvaluatedKey.Count > 0, "AWS SDK Query");
        }
        finally
        {
            _queryHandler.Capture = false;
        }
    }

    private sealed class EfficientDynamoDbHttpClientFactory(HttpMessageHandler handler) : EfficientDynamoDb.Configs.Http.IHttpClientFactory
    {
        public HttpClient CreateHttpClient() => new(handler, disposeHandler: false);
    }

    private sealed class AwsSdkHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);

        // Not cached: the SDK would otherwise share one HttpClient between clients with the same configuration string, and each
        // client here has a handler of its own.
        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }
}
