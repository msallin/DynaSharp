using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.Benchmarks;

/// <summary>
/// <see cref="DynamoDbRequests.GetItemAsync"/> and <see cref="DynamoDbRequests.QueryAsync"/> end to end against a canned response:
/// serialization, hashing, signing, the HTTP stack up to the handler, and parsing. No network, so it measures the client overhead.
/// </summary>
[MemoryDiagnoser]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet needs an unsealed type and owns the lifetime through GlobalCleanup.")]
public class ClientBenchmarks
{
    // The attributes of an order: ten attributes of the common types, about 400 bytes as DynamoDB sends them.
    private const string ItemAttributes = """
        {"pk":{"S":"USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19"},"sk":{"S":"ORDER#2026-10-31T12:34:56.789Z"},"type":{"S":"Order"},
        "orderId":{"N":"9182736455"},"total":{"N":"1249.95"},"currency":{"S":"CHF"},"shipped":{"BOOL":true},"giftNote":{"NULL":true},
        "tags":{"SS":["priority","fragile","insured"]},"address":{"M":{"street":{"S":"Bahnhofstrasse 1"},"city":{"S":"Zürich"},"zip":{"S":"8001"}}}}
        """;

    // A full page: Query reads at most 1 MB, and the items of a typical partition are small, so a page of a hundred is realistic.
    private const int QueryPageSize = 100;

    private StubHandler _handler = null!;
    private DynamoDbClient _client = null!;
    private GetItemRequest _request = null!;
    private StubHandler _queryHandler = null!;
    private DynamoDbClient _queryClient = null!;
    private QueryRequest _queryRequest = null!;

    [GlobalSetup]
    public void Setup()
    {
        // The handlers answer every request with the same response and record nothing, so their own cost stays small.
        _handler = new StubHandler { RecordRequests = false, DefaultResponse = StubResponse.Json("{\"Item\":" + ItemAttributes + "}") };
        _client = CreateClient(_handler);
        _request = new GetItemRequest
        {
            TableName = "Orders",
            Key = new Dictionary<string, AttributeValue>
            {
                ["pk"] = "USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19",
                ["sk"] = "ORDER#2026-10-31T12:34:56.789Z",
            },
        };

        _queryHandler = new StubHandler { RecordRequests = false, DefaultResponse = StubResponse.Json(CreateQueryPage()) };
        _queryClient = CreateClient(_queryHandler);
        _queryRequest = new QueryRequest
        {
            TableName = "Orders",
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :prefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = "USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19",
                [":prefix"] = "ORDER#",
            },
            Limit = QueryPageSize,
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _handler.Dispose();
        _queryClient.Dispose();
        _queryHandler.Dispose();
    }

    [Benchmark]
    public Task<GetItemResponse> GetItem() => _client.GetItemAsync(_request);

    [Benchmark]
    public Task<QueryResponse> Query() => _queryClient.QueryAsync(_queryRequest);

    private static DynamoDbClient CreateClient(StubHandler handler) => new(new DynamoDbClientOptions
    {
        Region = AwsRegion.FromName("eu-central-2"),
        Credentials = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")),
        HttpHandler = handler,
    });

    private static string CreateQueryPage()
    {
        var page = new StringBuilder("{\"Items\":[");
        for (var i = 0; i < QueryPageSize; i++)
        {
            page.Append(i == 0 ? string.Empty : ",").Append(ItemAttributes);
        }

        return page.Append("],\"Count\":").Append(QueryPageSize.ToString(CultureInfo.InvariantCulture))
            .Append(",\"ScannedCount\":").Append(QueryPageSize.ToString(CultureInfo.InvariantCulture))
            .Append(",\"LastEvaluatedKey\":{\"pk\":{\"S\":\"USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19\"},\"sk\":{\"S\":\"ORDER#2026-10-31T12:34:56.789Z\"}}}")
            .ToString();
    }
}
