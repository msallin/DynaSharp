using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>The condition <c>begins_with</c>.</summary>
public readonly struct BeginsWith : ICondition
{
    private readonly string _path;
    private readonly string _prefix;

    internal BeginsWith(string path, string prefix)
    {
        _path = path;
        _prefix = prefix;
    }

    /// <summary>Puts the condition on the heap, so that it composes with a <see cref="Condition"/>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(BeginsWith condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Text("begins_with("u8);
        sink.Path(_path);
        sink.Text(", "u8);
        sink.Value<string, StringConverter>(_prefix);
        sink.Text(")"u8);
    }
}
