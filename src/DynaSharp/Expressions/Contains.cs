using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>The condition <c>contains</c>, as <c>Order.Attrs.Tags.Contains("gift")</c> makes it.</summary>
/// <typeparam name="T">The type of the operand: the element of a set or a list, or the substring of a string.</typeparam>
/// <typeparam name="TConverter">The converter of the operand.</typeparam>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public readonly struct Contains<T, TConverter> : ICondition
    where TConverter : IDynamoDbConverter<T>
{
    private readonly string _path;
    private readonly T _operand;

    internal Contains(string path, T operand)
    {
        _path = path;
        _operand = operand;
    }

    /// <summary>Wraps the condition in a <see cref="Condition"/>, so it can be kept in a field or combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(Contains<T, TConverter> condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Text("contains("u8);
        sink.Path(_path);
        sink.Text(", "u8);
        sink.Value<T, TConverter>(_operand);
        sink.Text(")"u8);
    }
}
