namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="CreateTableRequest"/>.
/// </summary>
public sealed record CreateTableResponse
{
    /// <summary>
    /// Gets the description of the new table, normally in the state <see cref="TableStatus.Creating"/>. Poll
    /// <c>DescribeTable</c> until the table is <see cref="TableStatus.Active"/>. DynamoDB sends one with every response: <see langword="null"/> only if the response omits it.
    /// </summary>
    public TableDescription? TableDescription { get; init; }
}
