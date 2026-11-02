namespace DynaSharp;

/// <summary>
/// A wait for a table and its global secondary indexes to turn <c>ACTIVE</c> ran out of time: <see cref="Table{T}.WaitUntilActiveAsync(TimeSpan, CancellationToken)"/>, or
/// <see cref="CreateTableCommand.WaitUntilActive(TimeSpan)"/>. It is a <see cref="TimeoutException"/>, so a <c>catch (TimeoutException)</c> around a wait keeps working, and it says what the table was doing at the last look.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here says that the table will not turn active: restoring a table, or the backfill of an index on a table with much data, can take longer than a wait, and DynamoDB goes on by itself, so wait again with a
/// longer timeout. A status such as <see cref="TableStatus.Deleting"/> says otherwise. <see cref="LastStatus"/> tells these apart, and a <see langword="null"/> there means that the last look did not find the table, which can be a
/// wrong name, and then <see cref="Exception.InnerException"/> is the <see cref="ResourceNotFoundException"/>. <see cref="PendingIndex"/> names an index that held the wait up, which can happen with a table that reads
/// <see cref="TableStatus.Active"/>.
/// </para>
/// <para>
/// A single poll that runs out of <see cref="DynamoDbClientOptions.AttemptTimeout"/> on its last attempt is no such end of a wait, and surfaces as the plain <see cref="TimeoutException"/> of the pipeline.
/// </para>
/// </remarks>
public sealed class TableNotActiveException : TimeoutException
{
    /// <summary>Initializes a new instance of the <see cref="TableNotActiveException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="tableName">The name of the table.</param>
    /// <param name="lastStatus">The status the last look found, or <see langword="null"/> if it did not find the table.</param>
    /// <param name="pendingIndex">The global secondary index that was not active at the last look, or <see langword="null"/> if there was none.</param>
    /// <param name="innerException">The failure of the last look, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    public TableNotActiveException(string message, string tableName, TableStatus? lastStatus, string? pendingIndex = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        TableName = tableName;
        LastStatus = lastStatus;
        PendingIndex = pendingIndex;
    }

    /// <summary>Gets the name of the table, or its ARN, as the table was opened.</summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the status of the table at the last look, or <see langword="null"/> if that look did not find the table. A response without a description, or with a status this library does not know,
    /// is <see cref="TableStatus.Unknown"/>.
    /// </summary>
    public TableStatus? LastStatus { get; }

    /// <summary>
    /// Gets the name of a global secondary index that was not <c>ACTIVE</c> at the last look, the first one the description lists if there are several, then the first that an entity declares and the description does not
    /// list, which the message says, or <see langword="null"/> if none held the wait up. A table reads <see cref="TableStatus.Active"/> while an index backfills, so this can be set beside that status.
    /// </summary>
    public string? PendingIndex { get; }
}
