namespace DynaSharp.Testing.Fake;

/// <summary>
/// The state of one <see cref="FakeDynamoDb"/>: its tables by name, its clock and the gate every request and every direct call holds from start to end.
/// </summary>
/// <remarks>
/// One lock for everything keeps the fake simple and makes each request atomic, also a batch, which is stricter than DynamoDB and cannot fail a test that passes there. Nothing inside
/// the gate waits, so a test with many clients over one fake cannot deadlock on it.
/// </remarks>
/// <param name="clock">The clock of the creation times, of time to live and of the windows of client request tokens.</param>
internal sealed class FakeDatabase(TimeProvider clock)
{
    /// <summary>What DynamoDB answers an item operation on a table it does not have.</summary>
    public const string NoSuchTableForItems = "Cannot do operations on a non-existent table";

    /// <summary>Gets the gate.</summary>
    public Lock Gate { get; } = new();

    /// <summary>Gets the clock.</summary>
    public TimeProvider Clock { get; } = clock;

    /// <summary>Gets the tables by name, in ordinal order, which is also the order of <c>ListTables</c>.</summary>
    public SortedDictionary<string, FakeTable> Tables { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the client request tokens of the transactions that went through.</summary>
    public IdempotencyCache Tokens { get; } = new();

    /// <summary>Gets a table for an item operation.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>.</exception>
    public FakeTable GetTable(string name) =>
        Tables.TryGetValue(name, out var table) ? table : throw FakeError.ResourceNotFound(NoSuchTableForItems);

    /// <summary>Gets a table for a table operation, which DynamoDB answers with another message than an item operation.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>.</exception>
    public FakeTable DescribeTable(string name) =>
        Tables.TryGetValue(name, out var table) ? table : throw FakeError.ResourceNotFound($"Requested resource not found: Table: {name} not found");

    /// <summary>Adds a table.</summary>
    /// <param name="table">The table.</param>
    /// <exception cref="FakeError">A <c>ResourceInUseException</c>: a table of that name exists.</exception>
    public void Create(FakeTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!Tables.TryAdd(table.Name, table))
        {
            throw FakeError.ResourceInUse($"Table already exists: {table.Name}");
        }
    }

    /// <summary>Removes a table with its items. The name is free at once.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>.</exception>
    public FakeTable Delete(string name)
    {
        var table = DescribeTable(name);
        Tables.Remove(name);
        return table;
    }

    /// <summary>Drops every table and what belongs to them, and every client request token.</summary>
    public void Reset()
    {
        Tables.Clear();
        Tokens.Clear();
    }
}
