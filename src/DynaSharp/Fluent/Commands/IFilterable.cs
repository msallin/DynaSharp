using DynaSharp.Expressions;

namespace DynaSharp;

/// <summary>
/// A read that takes a filter. It is the way to write a helper that works on queries and scans alike, without knowing the command:
/// <code>
/// public static TCommand NotDeleted&lt;TCommand&gt;(this TCommand command)
///     where TCommand : IFilterable&lt;TCommand&gt;, allows ref struct =>
///     command.Filter(Attr("deletedAt").NotExists());
/// </code>
/// </summary>
/// <typeparam name="TSelf">The command.</typeparam>
public interface IFilterable<TSelf>
    where TSelf : IFilterable<TSelf>, allows ref struct
{
    /// <summary>
    /// Adds a filter. DynamoDB applies it after reading, so it shortens the result, not the read: capacity is charged for every item scanned, and a page
    /// can come back empty. A second filter means both must hold.
    /// </summary>
    /// <typeparam name="TCondition">The type of the condition.</typeparam>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf Filter<TCondition>(TCondition condition)
        where TCondition : ICondition;
}
