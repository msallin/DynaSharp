using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">Hi/lo sequence</see>: one <c>ADD</c> claims a block of numbers, which the process then hands out from memory, so a thousand ids cost a
/// write per block. Numbers are unique across processes. A process that stops leaves the rest of its block unused, and a retry of the <c>ADD</c> after a lost
/// response claims a block twice and wastes one: gaps, which are fine for ids and wrong for invoice numbers (see the gapless sequence).
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class HiLoAllocatorTests(Backend backend)
{
    [Test]
    public async Task NextAsync_TwoProcesses_HandOutUniqueNumbers()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        using var first = new HiLoAllocator(sandbox.Shop.Of<HiLoBlock>(), "orders", blockSize: 10);
        using var second = new HiLoAllocator(sandbox.Shop.Of<HiLoBlock>(), "orders", blockSize: 10);

        // Act
        var numbers = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => (i % 2 == 0 ? first : second).NextAsync().AsTask()));

        // Assert
        await Assert.That((numbers.Distinct().Count(), numbers.Min(), numbers.Max())).IsEqualTo((40, 1L, 40L));
    }

    [Test]
    public async Task NextAsync_ProcessStoppedMidBlock_LeavesAGap()
    {
        // Arrange (the first process takes three numbers of its block, then stops)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        using var stopped = new HiLoAllocator(sandbox.Shop.Of<HiLoBlock>(), "orders", blockSize: 10);
        for (var i = 0; i < 3; i++)
        {
            await stopped.NextAsync();
        }

        // Act
        using var other = new HiLoAllocator(sandbox.Shop.Of<HiLoBlock>(), "orders", blockSize: 10);
        var next = await other.NextAsync();

        // Assert (4 to 10 are never handed out)
        await Assert.That(next).IsEqualTo(11L);
    }
}

/// <summary>Hands out numbers from a block that one <c>ADD</c> claimed. Thread-safe; one per process and sequence.</summary>
/// <param name="blocks">The table of the blocks.</param>
/// <param name="name">The name of the sequence.</param>
/// <param name="blockSize">How many numbers one write claims.</param>
internal sealed class HiLoAllocator(Table<HiLoBlock> blocks, string name, int blockSize) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _next;
    private long _last = -1;

    public async ValueTask<long> NextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_next > _last)
            {
                // The retries of the client may claim a block twice after a lost response; that only wastes a block.
                var hi = await blocks.Update(HiLoBlock.Keys.Of(name)).Add(HiLoBlock.Attrs.Hi, (long)blockSize).Return(HiLoBlock.Attrs.Hi).SendAsync(cancellationToken);
                _next = hi - blockSize + 1;
                _last = hi;
            }

            return _next++;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

/// <summary>The highest number any process has claimed of a sequence.</summary>
[DynamoDbEntity]
[Discriminator("HILO")]
[PartitionKey("HILO#{Name}")]
[SortKey("HILO")]
internal sealed partial record HiLoBlock
{
    public required string Name { get; init; }

    public long Hi { get; init; }
}
