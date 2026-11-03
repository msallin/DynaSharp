using System.ComponentModel;

namespace DynaSharp.Expressions;

/// <summary>Either condition, in parentheses, so it composes with anything. A chain of these is one group: <c>(a OR b OR c)</c>.</summary>
/// <typeparam name="TLeft">The first condition.</typeparam>
/// <typeparam name="TRight">The second condition.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Or<TLeft, TRight> : ICondition
    where TLeft : ICondition
    where TRight : ICondition
{
    private readonly TLeft _left;
    private readonly TRight _right;

    internal Or(TLeft left, TRight right)
    {
        _left = left;
        _right = right;
    }

    /// <summary>Puts the condition on the heap, so that it composes with a <see cref="Condition"/>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(Or<TLeft, TRight> condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink) => ExpressionSink.WriteGroup(ref sink, _left, _right, ExpressionGroup.Or);
}
