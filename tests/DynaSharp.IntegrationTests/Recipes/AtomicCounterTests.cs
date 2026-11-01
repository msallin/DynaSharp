using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../spec/patterns.md#patterns">Atomic counter</see>: <c>ADD</c> counts on the server, so concurrent writers need no read and no lock, and each gets a number of its own. A retry after a lost
/// response counts twice, so an exact counter gets one attempt. One item takes about 1000 write units a second; beyond that, see the sharded counter.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class AtomicCounterTests(Backend backend)
{
    private const string Home = "/home";

    [Test]
    public async Task Add_ConcurrentWriters_CountsEveryOneAndGivesEachANumberOfItsOwn()
    {
        // Arrange
        const int Writers = 16;
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var counters = sandbox.Shop.Of<PageViews>();

        // Act
        var numbers = await Task.WhenAll(Enumerable.Range(0, Writers).Select(async _ => await CountAsync(counters)));

        // Assert
        await Assert.That(numbers.Order().SequenceEqual(Enumerable.Range(1, Writers).Select(static number => (long)number))).IsTrue();
    }

    [Test]
    public async Task Add_RetryAfterALostResponse_CountsTwice()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        using var flaky = new LostResponseClient(sandbox.Endpoint);

        // Act (one call, which is allowed its default attempts)
        var count = await flaky.Client.Table<PageViews>(sandbox.Shop.Name).Update(PageViews.Keys.Of(Home)).Add(PageViews.Attrs.Count, 1L).Return(PageViews.Attrs.Count).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(2L);
    }

    [Test]
    public async Task Add_OneAttemptAndALostResponse_CountsOnceAndFails()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        using var flaky = new LostResponseClient(sandbox.Endpoint);
        var counters = flaky.Client.Table<PageViews>(sandbox.Shop.Name);

        // Act
        var failure = await Items.FailureOfAsync<HttpRequestException>(async () => await CountAsync(counters));

        // Assert (the caller cannot tell whether it counted, which is the price of exactly-once)
        await Assert.That((failure is not null, (await counters.Get(PageViews.Keys.Of(Home)).Consistent().ItemAsync())!.Count)).IsEqualTo((true, 1L));
    }

    private static async Task<long> CountAsync(Table<PageViews> counters) =>
        await counters.Update(PageViews.Keys.Of(Home)).Add(PageViews.Attrs.Count, 1L).MaxAttempts(1).Return(PageViews.Attrs.Count).SendAsync();
}

/// <summary>A counter that only updates write. An update does not store the path, and a read takes it from the key.</summary>
[DynamoDbEntity]
[Discriminator("PAGE_VIEWS")]
[PartitionKey("PAGE#{Path}")]
[SortKey("VIEWS")]
internal sealed partial record PageViews
{
    public required string Path { get; init; }

    public long Count { get; init; }
}
