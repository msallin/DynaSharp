using DynaSharp.Expressions;

namespace DynaSharp;

/// <summary>Composes struct conditions: <c>Attr("a").Eq(1).And(Attr("b").Gt(2))</c>. Nothing is allocated.</summary>
public static class ConditionExtensions
{
    /// <summary>Makes a condition that holds when both hold.</summary>
    /// <typeparam name="TLeft">The type of the first condition.</typeparam>
    /// <typeparam name="TRight">The type of the second condition.</typeparam>
    /// <param name="left">The first condition.</param>
    /// <param name="right">The second condition.</param>
    /// <returns>The condition.</returns>
    public static And<TLeft, TRight> And<TLeft, TRight>(this TLeft left, TRight right)
        where TLeft : ICondition
        where TRight : ICondition => new(left, right);

    /// <summary>Makes a condition that holds when either holds.</summary>
    /// <typeparam name="TLeft">The type of the first condition.</typeparam>
    /// <typeparam name="TRight">The type of the second condition.</typeparam>
    /// <param name="left">The first condition.</param>
    /// <param name="right">The second condition.</param>
    /// <returns>The condition.</returns>
    public static Or<TLeft, TRight> Or<TLeft, TRight>(this TLeft left, TRight right)
        where TLeft : ICondition
        where TRight : ICondition => new(left, right);

    /// <summary>Makes a condition that holds when another one does not.</summary>
    /// <typeparam name="TInner">The type of the condition.</typeparam>
    /// <param name="condition">The other condition.</param>
    /// <returns>The opposite.</returns>
    public static Not<TInner> Negate<TInner>(this TInner condition)
        where TInner : ICondition => new(condition);
}
