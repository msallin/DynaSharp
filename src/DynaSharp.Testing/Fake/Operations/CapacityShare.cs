namespace DynaSharp.Testing.Fake;

/// <summary>The capacity one item of a batch or a transaction consumed on its table, which <see cref="WriteCapacity.WriteAll"/> sums per table.</summary>
/// <param name="TableName">The table.</param>
/// <param name="TableUnits">The units of the table.</param>
/// <param name="Indexes">The units of the indexes.</param>
internal readonly record struct CapacityShare(string TableName, double TableUnits, IReadOnlyList<IndexUnits> Indexes)
{
    /// <summary>Gets the share of a write, as <see cref="WriteCapacity.Units(WriteOutcome)"/> and <see cref="WriteCapacity.IndexWriteUnits"/> count it.</summary>
    /// <param name="table">The table.</param>
    /// <param name="outcome">The write.</param>
    /// <param name="factor">2 in a transaction, which writes every item twice, to prepare and to commit; else 1.</param>
    /// <returns>The share.</returns>
    public static CapacityShare OfWrite(FakeTable table, WriteOutcome outcome, int factor)
    {
        return new CapacityShare(
            table.Name,
            factor * WriteCapacity.Units(outcome),
            [.. WriteCapacity.IndexWriteUnits(table, outcome.OldImage, outcome.NewImage).Select(index => index with { Units = factor * index.Units })]);
    }
}
