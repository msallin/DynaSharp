using System.ComponentModel;
using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>A comparison of an attribute with a value, as <c>Attr("status").Eq("open")</c> makes it.</summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <typeparam name="TConverter">The converter of the value.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Compare<T, TConverter> : ICondition
    where TConverter : IDynamoDbConverter<T>
{
    private readonly string _path;
    private readonly ComparisonOperator _comparison;
    private readonly T _value;

    internal Compare(string path, ComparisonOperator comparison, T value)
    {
        _path = path;
        _comparison = comparison;
        _value = value;
    }

    /// <summary>Wraps the condition in a <see cref="Condition"/>, so it can be kept in a field or combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(Compare<T, TConverter> condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Path(_path);
        sink.Text(_comparison switch
        {
            ComparisonOperator.Equal => " = "u8,
            ComparisonOperator.NotEqual => " <> "u8,
            ComparisonOperator.Less => " < "u8,
            ComparisonOperator.LessOrEqual => " <= "u8,
            ComparisonOperator.Greater => " > "u8,
            _ => " >= "u8,
        });
        sink.Value<T, TConverter>(_value);
    }
}
