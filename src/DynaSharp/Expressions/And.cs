namespace DynaSharp.Expressions;

/// <summary>Both conditions, in parentheses, so it composes with anything.</summary>
/// <typeparam name="TLeft">The first condition.</typeparam>
/// <typeparam name="TRight">The second condition.</typeparam>
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

    /// <summary>Puts the condition on the heap, so that it composes with a <see cref="Condition"/>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(And<TLeft, TRight> condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Text("("u8);
        _left.Render(ref sink);
        sink.Text(" AND "u8);
        _right.Render(ref sink);
        sink.Text(")"u8);
    }
}
