using System.ComponentModel;

namespace DynaSharp.Expressions;

/// <summary>The opposite of a condition.</summary>
/// <typeparam name="TInner">The condition.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Not<TInner> : ICondition
    where TInner : ICondition
{
    private readonly TInner _inner;

    internal Not(TInner inner)
    {
        _inner = inner;
    }

    /// <summary>Wraps the condition in a <see cref="Condition"/>, so it can be kept in a field or combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(Not<TInner> condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Text("NOT ("u8);
        ExpressionSink.WriteInside(ref sink, _inner);
        sink.Text(")"u8);
    }
}
