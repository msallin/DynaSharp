using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class SegmentsTests
{
    public static IEnumerable<Func<AttributeValue>> Partitions()
    {
        yield return () => "user-1";
        yield return () => AttributeValue.FromNumber("42");
        yield return () => AttributeValue.FromBinary([0xFF, 0x00]);
    }

    [Test]
    [MethodDataSource(nameof(Partitions))]
    public async Task Of_Partition_IsTheSameEachTimeAndInRange(AttributeValue partition)
    {
        // Arrange
        // Act
        var first = Segments.Of(partition, 7);
        var second = Segments.Of(partition, 7);

        // Assert
        await Assert.That((first == second, first is >= 0 and < 7)).IsEqualTo((true, true));
    }

    [Test]
    public async Task Of_OneSegment_IsZero()
    {
        // Arrange
        // Act
        var segment = Segments.Of("anything", 1);

        // Assert
        await Assert.That(segment).IsEqualTo(0);
    }
}
