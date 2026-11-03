using System.ComponentModel;

namespace DynaSharp.Expressions;

/// <summary>The condition <c>attribute_exists</c> or <c>attribute_not_exists</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct AttributeExists : ICondition
{
    private readonly string _path;
    private readonly bool _exists;

    internal AttributeExists(string path, bool exists)
    {
        _path = path;
        _exists = exists;
    }

    /// <summary>Puts the condition on the heap, so that it composes with a <see cref="Condition"/>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(AttributeExists condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Text(_exists ? "attribute_exists("u8 : "attribute_not_exists("u8);
        sink.Path(_path);
        sink.Text(")"u8);
    }
}
