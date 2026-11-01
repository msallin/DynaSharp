using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// Pools that count and tables of their own for one test, so that what the test reads depends on nothing another test read before.
/// </summary>
internal sealed class TestPools
{
    private TestPools(int slots)
    {
        Pools = new(Values, Strings, Binaries, new NameTable(slots), new ShapeTable(slots));
    }

    public CountingArrayPool<AttributeValue> Values { get; } = new();

    public CountingArrayPool<string> Strings { get; } = new();

    public CountingArrayPool<ReadOnlyMemory<byte>> Binaries { get; } = new();

    public BuilderPools Pools { get; }

    /// <summary>Gets the number of arrays rented from any pool and not returned.</summary>
    public int Outstanding => Values.Outstanding + Strings.Outstanding + Binaries.Outstanding;

    /// <summary>Creates pools and tables.</summary>
    /// <param name="slots">The number of slots of each table, a power of two.</param>
    /// <returns>The pools.</returns>
    public static TestPools Create(int slots = 64) => new(slots);

    /// <summary>Reads one item.</summary>
    /// <param name="json">The DynamoDB JSON.</param>
    /// <returns>The item.</returns>
    public Item Read(string json)
    {
        var reader = new DynamoDbReader(Encoding.UTF8.GetBytes(json));
        return AttributeValueConverter.ReadItem(ref reader, Pools);
    }
}
