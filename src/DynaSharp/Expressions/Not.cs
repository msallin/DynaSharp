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

    /// <summary>Puts the condition on the heap, so that it composes with a <see cref="Condition"/>.</summary>
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
