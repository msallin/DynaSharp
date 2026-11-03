using System.Text;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// Which segment of a parallel scan a partition belongs to: a hash of its key value, the same on every run, modulo the number of segments. DynamoDB keeps its hash to itself, so all a
/// test may rely on is what it relies on there: every item is in exactly one segment.
/// </summary>
internal static class Segments
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    /// <summary>Gets the segment of a partition.</summary>
    /// <param name="partition">The value of the partition key, in its stored form.</param>
    /// <param name="totalSegments">The number of segments, at least 1.</param>
    /// <returns>A segment from 0 to <paramref name="totalSegments"/> - 1.</returns>
    public static int Of(AttributeValue partition, int totalSegments)
    {
        // FNV-1a over the bytes of the value. A number is in its canonical text, so equal numbers hash alike.
        var bytes = partition.Type == AttributeType.Binary ? partition.AsBinary().Span : Encoding.UTF8.GetBytes(partition.Type == AttributeType.String ? partition.AsString() : partition.AsNumber());
        var hash = OffsetBasis;
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * Prime;
        }

        return (int)(hash % (uint)totalSegments);
    }
}
