using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>The condition <c>begins_with</c>.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public readonly struct BeginsWith : ICondition
{
    private readonly string _path;
    private readonly string? _prefix;
    private readonly ReadOnlyMemory<byte> _binaryPrefix;

    internal BeginsWith(string path, string prefix)
    {
        _path = path;
        _prefix = prefix;
    }

    internal BeginsWith(string path, ReadOnlyMemory<byte> prefix)
    {
        _path = path;
        _binaryPrefix = prefix;
    }

    /// <summary>Wraps the condition in a <see cref="Condition"/>, so it can be kept in a field or combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(BeginsWith condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Text("begins_with("u8);
        sink.Path(_path);
        sink.Text(", "u8);
        if (_prefix is null)
        {
            sink.Value<ReadOnlyMemory<byte>, ReadOnlyMemoryConverter>(_binaryPrefix);
        }
        else
        {
            sink.Value<string, StringConverter>(_prefix);
        }

        sink.Text(")"u8);
    }
}
