using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using Dyna = DynaSharp.Requests;

namespace DynaSharp.Benchmarks;

/// <summary>
/// The calls of <see cref="ClientComparisonBenchmarks"/> against a handler that suspends before it answers. A response that
/// completes synchronously hides the async state machines of the client, which a call over a network always allocates, so these
/// numbers are what the pipeline costs in production. Time means little here (a yield goes through the thread pool); the allocations
/// are the point.
/// </summary>
[MemoryDiagnoser]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet needs an unsealed type and owns the lifetime through GlobalCleanup.")]
public class SuspendingHandlerBenchmarks
{
    private CannedHandler _getHandler = null!;
    private CannedHandler _putHandler = null!;
    private DynamoDbClient _get = null!;
    private DynamoDbClient _put = null!;
    private Dyna.GetItemRequest _getRequest = null!;
    private Dyna.PutItemRequest _putRequest = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _getHandler = new CannedHandler("{\"Item\":" + ClientComparisonBenchmarks.ItemAttributes + "}", suspend: true);
        _putHandler = new CannedHandler("{}", suspend: true);
        _get = Create(_getHandler);
        _put = Create(_putHandler);
        _getRequest = ClientComparisonBenchmarks.CreateDynaSharpGetRequest();
        _putRequest = ClientComparisonBenchmarks.CreateDynaSharpPutRequest();

        var item = (await GetItem()).Item;
        if (item?["address"].AsMap()["city"].AsString() != "Zürich")
        {
            throw new InvalidOperationException("The benchmark does not do the work it should: GetItem did not parse the item.");
        }

        await PutItem();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _get.Dispose();
        _put.Dispose();
        _getHandler.Dispose();
        _putHandler.Dispose();
    }

    [Benchmark]
    public Task<Dyna.GetItemResponse> GetItem() => _get.GetItemAsync(_getRequest);

    [Benchmark]
    public Task<Dyna.PutItemResponse> PutItem() => _put.PutItemAsync(_putRequest);

    private static DynamoDbClient Create(HttpMessageHandler handler) => new(new DynamoDbClientOptions
    {
        Region = AwsRegion.FromName("eu-central-2"),
        Credentials = new StaticCredentialsProvider(new AwsCredentials(ClientComparisonBenchmarks.AccessKey, ClientComparisonBenchmarks.SecretKey)),
        HttpHandler = handler,
    });
}
