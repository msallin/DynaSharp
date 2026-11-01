namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="UpdateTableRequest"/>.
/// </summary>
public sealed record UpdateTableResponse
{
    /// <summary>
    /// Gets the description of the table, normally in the state <see cref="TableStatus.Updating"/>. The description may still show the
    /// old settings for what DynamoDB has not applied yet; poll <c>DescribeTable</c> until the table is
    /// <see cref="TableStatus.Active"/>. DynamoDB sends one with every response: <see langword="null"/> only if the response omits it.
    /// </summary>
    public TableDescription? TableDescription { get; init; }
}
