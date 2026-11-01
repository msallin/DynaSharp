using System.ComponentModel;
using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>
/// A value with its converter, for a hole of an interpolated expression, as <c>Attr.Value(x)</c> makes it. It is how a value of a type the
/// holes do not know, such as an enum with a generated converter, gets into an expression.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <typeparam name="TConverter">The converter of the value.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Arg<T, TConverter>
    where TConverter : IDynamoDbConverter<T>
{
    internal Arg(T value)
    {
        Value = value;
    }

    internal T Value { get; }
}
