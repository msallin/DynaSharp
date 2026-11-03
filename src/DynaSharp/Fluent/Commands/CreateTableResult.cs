using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// What a <see cref="CreateTableCommand"/> returns: the table, and what this call changed about it. A program that creates its table at every start learns here whether this start
/// created it or turned time to live on, for a log line or a metric, without a call of its own.
/// </summary>
public sealed class CreateTableResult
{
    internal CreateTableResult(TableDescription table, bool created, bool timeToLiveEnabled)
    {
        Table = table;
        Created = created;
        TimeToLiveEnabled = timeToLiveEnabled;
    }

    /// <summary>
    /// Gets the description of the table: the one <c>CreateTable</c> answered, the active one after a wait, or, with <see cref="CreateTableCommand.IfNotExists"/>, the one the first look found. Time to live is
    /// no part of it, because <c>DescribeTable</c> does not report it.
    /// </summary>
    public TableDescription Table { get; }

    /// <summary>
    /// Gets a value indicating whether the <c>CreateTable</c> of this call created the table. It is <see langword="false"/> when <see cref="CreateTableCommand.IfNotExists"/> found it, at the first look or after a
    /// <see cref="ResourceInUseException"/> because another process created it first. A retry of a create whose response was lost is such a case too: the table may be this call's, and nothing tells the two apart.
    /// Without <see cref="CreateTableCommand.IfNotExists"/> a call that returns created the table, so this is <see langword="true"/>.
    /// </summary>
    public bool Created { get; }

    /// <summary>
    /// Gets a value indicating whether this call turned time to live on, with an <c>UpdateTimeToLive</c> that DynamoDB accepted: on a table it created, or on one it found without it. DynamoDB may still report
    /// <c>ENABLING</c> then. It is <see langword="false"/> without <see cref="CreateTableCommand.TimeToLive(string)"/>, when time to live was <c>ENABLED</c> or <c>ENABLING</c> on that attribute already, and when
    /// another instance of the program turned it on first, which DynamoDB answers with a <see cref="ValidationException"/> that the command takes for success.
    /// </summary>
    public bool TimeToLiveEnabled { get; }
}
