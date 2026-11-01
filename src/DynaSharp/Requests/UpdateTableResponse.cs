namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="UpdateTableRequest"/>.
/// </summary>
public sealed record UpdateTableResponse
{
    /// <summary>
    /// Gets the description of the table, normally in the state <see cref="TableStatus.Updating"/>. The description may still show the
    /// old settings for what DynamoDB has not applied yet; poll <c>DescribeTable</c> until the table is
    /// <see cref="TableStatus.Active"/>.
    /// </summary>
    public required TableDescription TableDescription { get; init; }
}
