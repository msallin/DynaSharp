using DynaSharp.Expressions;

namespace DynaSharp;

/// <summary>
/// A command that takes a condition. It is the way to write a helper that works on all of them, without knowing the
/// command:
/// <code>
/// public static TCommand OnlyIfOwned&lt;TCommand&gt;(this TCommand command, string owner)
///     where TCommand : IConditional&lt;TCommand&gt;, allows ref struct =>
///     command.If(Attr("owner").Eq(owner));
/// </code>
/// </summary>
/// <typeparam name="TSelf">The command.</typeparam>
public interface IConditional<TSelf>
    where TSelf : IConditional<TSelf>, allows ref struct
{
    /// <summary>Adds a condition. The write happens only if it holds, and a second condition means both must hold.</summary>
    /// <typeparam name="TCondition">The type of the condition.</typeparam>
    /// <param name="condition">The condition.</param>
    /// <returns>The command, which replaces this one.</returns>
    public TSelf If<TCondition>(TCondition condition)
        where TCondition : ICondition;
}
