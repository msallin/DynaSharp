using System.ComponentModel;
using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>A range condition, both bounds included.</summary>
/// <typeparam name="T">The type of the bounds.</typeparam>
/// <typeparam name="TConverter">The converter of the bounds.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Between<T, TConverter> : ICondition
    where TConverter : IDynamoDbConverter<T>
{
    private readonly string _path;
    private readonly T _low;
    private readonly T _high;

    internal Between(string path, T low, T high)
    {
        _path = path;
        _low = low;
        _high = high;
    }

    /// <summary>Wraps the condition in a <see cref="Condition"/>, so it can be kept in a field or combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.</summary>
    /// <param name="condition">The condition.</param>
    public static implicit operator Condition(Between<T, TConverter> condition) => Condition.Of(condition);

    /// <inheritdoc/>
    public void Render(ref ExpressionSink sink)
    {
        sink.Path(_path);
        sink.Text(" BETWEEN "u8);
        sink.Value<T, TConverter>(_low);
        sink.Text(" AND "u8);
        sink.Value<T, TConverter>(_high);
    }
}
