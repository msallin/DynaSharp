namespace DynaSharp.FakeServer;

/// <summary>
/// The CRC-32 of IEEE 802.3 (polynomial <c>0xEDB88320</c>, reflected), which DynamoDB sends in <c>x-amz-crc32</c> as an unsigned decimal. A table of 256 entries, so no package is needed.
/// </summary>
/// <remarks>The check value of the standard: <c>"123456789"</c> is <c>0xCBF43926</c>, 3421780262.</remarks>
internal static class Crc32
{
    private const uint Polynomial = 0xEDB88320;

    private static readonly uint[] Table = CreateTable();

    /// <summary>Computes the checksum of a body.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var entry = i;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1) != 0 ? Polynomial ^ (entry >> 1) : entry >> 1;
            }

            table[i] = entry;
        }

        return table;
    }
}
