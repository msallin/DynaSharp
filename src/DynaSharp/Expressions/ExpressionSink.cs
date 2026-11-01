using System.ComponentModel;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>Where a condition renders to: the expression text and the placeholder maps of one command.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly ref struct ExpressionSink
{
    private readonly ExpressionWriter _writer;
    private readonly PooledByteBuffer _text;

    internal ExpressionSink(ExpressionWriter writer, PooledByteBuffer text)
    {
        _writer = writer;
        _text = text;
    }

    /// <summary>Appends expression text as it is, for example <c>" AND "u8</c>.</summary>
    /// <param name="utf8Text">The text. It ends up in a JSON string, so anything JSON cannot carry as it is gets escaped.</param>
    public void Text(ReadOnlySpan<byte> utf8Text) => WireJson.AppendEncoded(_text, utf8Text);

    /// <summary>Appends a document path as placeholders.</summary>
    /// <param name="path">The path, such as <c>status</c>, <c>address.city</c> or <c>lines[2].sku</c>.</param>
    /// <exception cref="ArgumentException">The path is not valid.</exception>
    public void Path(string path) => _writer.AppendPath(_text, path);

    /// <summary>Appends a value as a placeholder. Equal values share one.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="value">The value.</param>
    public void Value<T, TConverter>(T value)
        where TConverter : IDynamoDbConverter<T> => _writer.AppendValue<T, TConverter>(_text, value);
}
