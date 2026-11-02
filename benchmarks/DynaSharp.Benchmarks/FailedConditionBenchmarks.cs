using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using DynaSharp.Credentials;
using DynaSharp.Fluent;
using DynaSharp.Testing;

namespace DynaSharp.Benchmarks;

/// <summary>
/// What a failed <c>Put</c> costs when the condition is routine, as in a create-if-absent: <see cref="Describe"/> is the search for the key alone, and <see cref="Put"/> is the whole call
/// against a stub. An item is a thousand attributes of about 390 bytes at its largest, near the limit of 400 KB, and its key comes first, as in an entity, or last, the worst a forward walk can meet.
/// </summary>
[MemoryDiagnoser]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet needs an unsealed type and owns the lifetime through GlobalCleanup.")]
public class FailedConditionBenchmarks
{
    private const int AttributeLength = 390;

    private static readonly string[] KeyNames = ["pk", "sk"];

    private readonly ConditionalCheckFailedException _failure = new("The conditional request failed");

    private StubHandler _handler = null!;
    private DynamoDbClient _client = null!;
    private Table<Item> _shop = null!;
    private Item _item = null!;
    private ArraySegment<byte> _body;

    /// <summary>Gets or sets how many attributes the item has beside its key.</summary>
    [Params(10, 250, 1000)]
    public int Attributes { get; set; }

    /// <summary>Gets or sets a value indicating whether the key attributes come after all others.</summary>
    [Params(false, true)]
    public bool KeyLast { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _item = CreateItem();
        _handler = new StubHandler { DefaultResponse = StubResponse.ConditionalCheckFailed() };
        _client = new DynamoDbClient(new DynamoDbClientOptions
        {
            Region = AwsRegion.FromName("eu-central-2"),
            Credentials = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY")),
            HttpHandler = _handler,
        });
        _shop = _client.Table("Shop", "pk", "sk");

        // The one request that is recorded is the body the search reads, as the client wrote it.
        Put().GetAwaiter().GetResult();
        _body = new ArraySegment<byte>(_handler.Requests[0].Body.ToArray());
        _handler.RecordRequests = false;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Benchmark]
    public Item? Describe()
    {
        FailedCondition.Describe(_failure, _body, KeyNames);
        return _failure.Key;
    }

    [Benchmark]
    public async Task<Item?> Put()
    {
        try
        {
            await _shop.Put(_item).IfNotExists().SendAsync();
            return null;
        }
        catch (ConditionalCheckFailedException failure)
        {
            return failure.Key;
        }
    }

    private static void AddKey(Item item)
    {
        item["pk"] = "USER#4f9c2a7e-0b1d-4c3e-9a8f-6d5e4c3b2a19";
        item["sk"] = "PROFILE";
    }

    private Item CreateItem()
    {
        var item = new Item(Attributes + KeyNames.Length);
        if (!KeyLast)
        {
            AddKey(item);
        }

        var text = new string('x', AttributeLength);
        for (var i = 0; i < Attributes; i++)
        {
            item["a" + i] = text;
        }

        if (KeyLast)
        {
            AddKey(item);
        }

        return item;
    }
}
