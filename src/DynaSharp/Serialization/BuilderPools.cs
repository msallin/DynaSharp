using System.Buffers;

namespace DynaSharp.Serialization;

/// <summary>
/// What <see cref="AttributeValueConverter"/> reads with: the array pools it collects elements in, and the tables that share attribute
/// names. Reading takes them as a parameter so that a test can hand in pools that count and tables of its own, instead of depending on
/// the process-wide ones.
/// </summary>
/// <param name="values">Pool for the elements of lists and the values of large items.</param>
/// <param name="strings">Pool for the elements of string and number sets and the names of large items.</param>
/// <param name="binaries">Pool for the elements of binary sets.</param>
/// <param name="names">The table of attribute names.</param>
/// <param name="shapes">The table of item shapes.</param>
internal sealed class BuilderPools(
    ArrayPool<AttributeValue> values,
    ArrayPool<string> strings,
    ArrayPool<ReadOnlyMemory<byte>> binaries,
    NameTable names,
    ShapeTable shapes)
{
    /// <summary>Gets what production code uses: the shared pool of each element type and the process-wide tables.</summary>
    public static BuilderPools Shared { get; } = new(
        ArrayPool<AttributeValue>.Shared,
        ArrayPool<string>.Shared,
        ArrayPool<ReadOnlyMemory<byte>>.Shared,
        NameTable.Shared,
        ShapeTable.Shared);

    /// <summary>Gets the pool for the elements of lists and the values of large items.</summary>
    public ArrayPool<AttributeValue> Values { get; } = values;

    /// <summary>Gets the pool for the elements of string and number sets and the names of large items.</summary>
    public ArrayPool<string> Strings { get; } = strings;

    /// <summary>Gets the pool for the elements of binary sets.</summary>
    public ArrayPool<ReadOnlyMemory<byte>> Binaries { get; } = binaries;

    /// <summary>Gets the table of attribute names.</summary>
    public NameTable Names { get; } = names;

    /// <summary>Gets the table of item shapes.</summary>
    public ShapeTable Shapes { get; } = shapes;
}
