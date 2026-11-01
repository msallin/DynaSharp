namespace DynaSharp.Requests;

/// <summary>
/// The result of <see cref="DeleteTableRequest"/>.
/// </summary>
public sealed record DeleteTableResponse
{
    /// <summary>
    /// Gets the description of the table, normally in the state <see cref="TableStatus.Deleting"/>. Poll <c>DescribeTable</c> until
    /// it fails with <see cref="ResourceNotFoundException"/> to know that the table is gone. DynamoDB sends one with every response: <see langword="null"/> only if the response omits it.
    /// </summary>
    public TableDescription? TableDescription { get; init; }
}
