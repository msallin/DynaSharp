namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="CreateTableRequest"/>.
/// </summary>
public sealed record CreateTableResponse
{
    /// <summary>
    /// Gets the description of the new table, normally in the state <see cref="TableStatus.Creating"/>. Poll
    /// <c>DescribeTable</c> until the table is <see cref="TableStatus.Active"/>.
    /// </summary>
    public required TableDescription TableDescription { get; init; }
}
