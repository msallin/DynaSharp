using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Commands;
using DynaSharp.Mapping;

namespace DynaSharp.IntegrationTests.Recipes;

/// <summary>
/// <see href="../../../docs/patterns.md">HyperLogLog</see>: distinct counts of any size in a fixed kilobyte, stored as a binary attribute. DynamoDB cannot merge bytes, so a writer merges on the client, and <c>[Version]</c> keeps two
/// merges from losing each other.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class HyperLogLogTests(Backend backend)
{
    private const int Registers = 1024;
    private const string Today = "2026-11-01";

    [Test]
    public async Task MergeAsync_ConcurrentWritersWithOverlappingValues_EstimateTheDistinctCount()
    {
        // Arrange (four writers of 1000 values each, of 2500 distinct ones)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var sketches = sandbox.Shop.Of<VisitorSketch>();

        // Act
        await Task.WhenAll(Enumerable.Range(0, 4).Select(writer => MergeAsync(sketches, Sketch(writer * 500, 1000))));
        var stored = await sketches.Get(VisitorSketch.Keys.Of(Today)).Consistent().ItemAsync();

        // Assert (the standard error of 1024 registers is 3 %, and the values are fixed, so this holds on every run)
        await Assert.That(Estimate(stored!.Registers.Span)).IsBetween(2300, 2700);
    }

    [Test]
    public async Task MergeAsync_SameValuesAgain_ChangesNothing()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var sketches = sandbox.Shop.Of<VisitorSketch>();
        await MergeAsync(sketches, Sketch(0, 1000));
        var before = await sketches.Get(VisitorSketch.Keys.Of(Today)).Consistent().ItemAsync();

        // Act
        await MergeAsync(sketches, Sketch(0, 1000));
        var after = await sketches.Get(VisitorSketch.Keys.Of(Today)).Consistent().ItemAsync();

        // Assert
        await Assert.That(after!.Registers.Span.SequenceEqual(before!.Registers.Span)).IsTrue();
    }

    // A writer that merges first makes this one merge with that. Four writers need four runs at most, which the default allows.
    private static Task MergeAsync(Table<VisitorSketch> sketches, byte[] local) =>
        sketches.RetryOnConflictAsync(async ct =>
        {
            var stored = await sketches.Get(VisitorSketch.Keys.Of(Today)).Consistent().ItemAsync(ct) ?? new VisitorSketch { Day = Today, Registers = new byte[Registers] };
            var merged = new byte[Registers];
            for (var i = 0; i < Registers; i++)
            {
                merged[i] = Math.Max(local[i], stored.Registers.Span[i]);
            }

            await sketches.Put(stored with { Registers = merged }).SendAsync(ct);
        });

    private static byte[] Sketch(int firstVisitor, int visitors)
    {
        var registers = new byte[Registers];
        for (var visitor = firstVisitor; visitor < firstVisitor + visitors; visitor++)
        {
            var hash = BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"visitor{visitor}")));
            var register = (int)(hash >> 54);
            var rank = (byte)(BitOperations.LeadingZeroCount(hash << 10) + 1);
            registers[register] = Math.Max(registers[register], rank);
        }

        return registers;
    }

    // Flajolet's estimate, with linear counting while registers are still empty.
    private static double Estimate(ReadOnlySpan<byte> registers)
    {
        var sum = 0.0;
        var empty = 0;
        foreach (var rank in registers)
        {
            sum += Math.Pow(2, -rank);
            empty += rank == 0 ? 1 : 0;
        }

        var estimate = 0.7213 / (1 + (1.079 / Registers)) * Registers * Registers / sum;
        return estimate <= 2.5 * Registers && empty > 0 ? Registers * Math.Log((double)Registers / empty) : estimate;
    }
}

[DynamoDbEntity]
[Discriminator("SKETCH")]
[PartitionKey("VISITORS#{Day}")]
[SortKey("SKETCH")]
internal sealed partial record VisitorSketch
{
    public required string Day { get; init; }

    public required ReadOnlyMemory<byte> Registers { get; init; }

    [Version]
    public long Revision { get; init; }
}
