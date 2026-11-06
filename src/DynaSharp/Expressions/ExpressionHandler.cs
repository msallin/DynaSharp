using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>
/// Turns an interpolated string such as <c>$"{Attr("status")} = {"open"} AND size({Attr("tags")}) &lt; {10}"</c> into a DynamoDB expression: the literal parts are expression text as the
/// DynamoDB documentation writes it, an <see cref="Attr"/> in a hole (an interpolated value) becomes a <c>#n</c> name placeholder and any other interpolated value a <c>:v</c> value placeholder.
/// </summary>
/// <remarks>
/// A plain string in a hole is a value, so a forgotten <c>Attr(...)</c> around a name, as in <c>$"{"status"} = {"open"}"</c>, renders
/// <c>:v0 = :v1</c>. DynamoDB accepts that as a comparison of two values, so an expression that names no attribute throws an
/// <see cref="ArgumentException"/> here instead. A forgotten <c>Attr(...)</c> on one side of an expression that names another attribute stays
/// undetected at run time; the analyzer reports the constant case as <see href="https://msallin.github.io/DynaSharp/diagnostics.html#expressions">DYNA210</see>.
/// </remarks>
[InterpolatedStringHandler]
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly ref struct ExpressionHandler
{
    private readonly ExpressionWriter _writer;
    private readonly PooledByteBuffer _text;

    /// <summary>Initializes a new instance of the <see cref="ExpressionHandler"/> struct. The compiler calls this.</summary>
    /// <param name="literalLength">The number of literal characters.</param>
    /// <param name="formattedCount">The number of holes.</param>
    /// <param name="command">The command the expression belongs to.</param>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ExpressionHandler(int literalLength, int formattedCount, CommandHandle command)
    {
        // The handler runs before the method it belongs to, so a stale command must not reach a state that has a new owner.
        var state = CommandState.Check(command.State, command.Version, command.Operation, command.Table);
        _writer = state.Expressions;
        _text = _writer.StartPending();
    }

    /// <summary>Appends literal expression text. The compiler calls this.</summary>
    /// <param name="text">The text.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendLiteral(string text) => WireJson.AppendEncoded(_text, text);

    /// <summary>Appends an attribute as placeholders. The compiler calls this.</summary>
    /// <param name="attribute">The attribute.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendFormatted(Attr attribute) => _writer.AppendPath(_text, attribute.Path);

    /// <summary>Appends an attribute as placeholders. The compiler calls this.</summary>
    /// <typeparam name="T">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendFormatted<T, TConverter>(Attr<T, TConverter> attribute)
        where TConverter : IDynamoDbConverter<T> => _writer.AppendPath(_text, attribute.Path);

    /// <summary>Appends a value as a placeholder. The compiler calls this.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter of the value.</typeparam>
    /// <param name="value">The value.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendFormatted<T, TConverter>(Arg<T, TConverter> value)
        where TConverter : IDynamoDbConverter<T> => _writer.AppendValue<T, TConverter>(_text, value.Value);

    /// <summary>Appends a value as a placeholder. The compiler calls this.</summary>
    /// <param name="value">The value.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendFormatted(AttributeValue value) => _writer.AppendValue<AttributeValue, AttributeValueConverter>(_text, value);

    /// <summary>Appends a number as a placeholder. The compiler calls this.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="value">The number.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AppendFormatted<TNumber>(TNumber value)
        where TNumber : INumberBase<TNumber> => _writer.AppendValue<TNumber, NumberConverter<TNumber>>(_text, value);
}
