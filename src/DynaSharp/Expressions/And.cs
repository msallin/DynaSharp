using System.ComponentModel;

namespace DynaSharp.Expressions;

/// <summary>Both conditions, in parentheses, so it composes with anything. A chain of these is one group: <c>(a AND b AND c)</c>.</summary>
/// <typeparam name="TLeft">The first condition.</typeparam>
/// <typeparam name="TRight">The second condition.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct And<TLeft, TRight> : ICondition
    where TLeft : ICondition
    where TRight : ICondition
{
    private readonly TLeft _left;
    private readonly TRight _right;

    internal And(TLeft left, TRight right)
    {
        _left = left;
        _right = right;
    }

    /// <summary>Wraps the condition in a <see cref="Condition"/>, so it can be kept in a field or combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(And<TLeft, TRight> condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink) => ExpressionSink.WriteGroup(ref sink, _left, _right, ExpressionGroup.And);
}
