using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The capacity arithmetic of the Developer Guide: a read is 1 unit per 4 KB, rounded up, and half of that when eventually consistent; a write is 1 unit per 1 KB of the larger of the
/// old and the new image; an index pays for what it holds of a change.
/// </summary>
internal static class Capacity
{
    /// <summary>Gets the units of a read.</summary>
    /// <param name="sizeBytes">The bytes read.</param>
    /// <param name="consistent"><see langword="true"/> for a strongly consistent read.</param>
    /// <returns>The units.</returns>
    public static double ReadUnits(long sizeBytes, bool consistent) => throw FakeError.Unsupported("the capacity units of a read");

    /// <summary>Gets the units of a write to a table.</summary>
    /// <param name="oldSizeBytes">The size of the old image, 0 for none.</param>
    /// <param name="newSizeBytes">The size of the new image, 0 for a delete.</param>
    /// <returns>The units.</returns>
    public static double WriteUnits(long oldSizeBytes, long newSizeBytes) => throw FakeError.Unsupported("the capacity units of a write");

    /// <summary>Gets the units each index of a table pays for a write.</summary>
    /// <param name="table">The table.</param>
    /// <param name="oldImage">The item before, or <see langword="null"/>.</param>
    /// <param name="newImage">The item after, or <see langword="null"/>.</param>
    /// <returns>The units per index that the write touches.</returns>
    public static IReadOnlyList<IndexUnits> IndexWriteUnits(FakeTable table, Item? oldImage, Item? newImage) => throw FakeError.Unsupported("the capacity units of an index");

    /// <summary>Writes <c>ConsumedCapacity</c> of one table as <c>TOTAL</c> or <c>INDEXES</c> ask for it.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="mode">What the request asked for, not <see cref="ReturnConsumedCapacity.None"/>.</param>
    /// <param name="tableName">The table.</param>
    /// <param name="tableUnits">The units of the table.</param>
    /// <param name="isRead"><see langword="true"/> for the units of a read.</param>
    /// <param name="indexes">The units of the indexes.</param>
    public static void Write(Utf8JsonWriter response, ReturnConsumedCapacity mode, string tableName, double tableUnits, bool isRead, IReadOnlyList<IndexUnits> indexes) =>
        throw FakeError.Unsupported("ReturnConsumedCapacity");
}
