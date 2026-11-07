using System.ComponentModel;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp.Expressions;

/// <summary>Where a condition renders to: the expression text and the placeholder maps of one command.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct ExpressionSink
{
    private readonly ExpressionWriter _writer;
    private readonly PooledByteBuffer _text;

    // The group written last: a term that is exactly this text is a group, whatever its type, which a generic condition cannot ask without a box.
    private int _groupStart;
    private int _groupEnd;
    private ExpressionGroup _group;

    internal ExpressionSink(ExpressionWriter writer, PooledByteBuffer text)
    {
        _writer = writer;
        _text = text;
    }

    /// <summary>Appends expression text as it is, for example <c>" AND "u8</c>.</summary>
    /// <param name="utf8Text">The text. It ends up in a JSON string, so anything JSON cannot carry as it is gets escaped.</param>
    public readonly void Text(ReadOnlySpan<byte> utf8Text) => WireJson.AppendEncoded(_text, utf8Text);

    /// <summary>Appends a document path as placeholders.</summary>
    /// <param name="path">The path, such as <c>status</c>, <c>address.city</c> or <c>lines[2].sku</c>.</param>
    /// <exception cref="ArgumentException">The path is not valid.</exception>
    public readonly void Path(string path) => _writer.Placeholders.AppendPath(_text, path);

    /// <summary>Appends a value as a placeholder. Equal values share one.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="value">The value.</param>
    public readonly void Value<T, TConverter>(T value)
        where TConverter : IDynamoDbConverter<T> => _writer.Placeholders.AppendValue<T, TConverter>(_text, value);

    /// <summary>
    /// Writes two conditions as one group. A term that is a group of the same operator gives up its parentheses, so <c>a.Or(b).Or(c)</c> is <c>(a OR b OR c)</c> and not <c>((a OR b) OR c)</c>,
    /// which means the same, because both operators are associative. A group of the other operator keeps them, and so does text a custom condition wrote, which is not known to be a group.
    /// </summary>
    /// <typeparam name="TLeft">The type of the first condition.</typeparam>
    /// <typeparam name="TRight">The type of the second condition.</typeparam>
    /// <param name="sink">The sink.</param>
    /// <param name="left">The first condition.</param>
    /// <param name="right">The second condition.</param>
    /// <param name="group">What joins them.</param>
    internal static void WriteGroup<TLeft, TRight>(ref ExpressionSink sink, in TLeft left, in TRight right, ExpressionGroup group)
        where TLeft : ICondition
        where TRight : ICondition
    {
        var start = sink._text.WrittenCount;
        sink.Text("("u8);
        left.Render(ref sink);
        sink.Unwrap(start + 1, group);
        sink.Text(group == ExpressionGroup.And ? " AND "u8 : " OR "u8);
        var term = sink._text.WrittenCount;
        right.Render(ref sink);
        sink.Unwrap(term, group);
        sink.Text(")"u8);
        sink._groupStart = start;
        sink._groupEnd = sink._text.WrittenCount;
        sink._group = group;
    }

    /// <summary>Writes a condition inside parentheses that are there anyway, as those of <c>NOT (...)</c>: a group of any operator gives up its own, because DynamoDB rejects redundant ones.</summary>
    /// <typeparam name="TInner">The type of the condition.</typeparam>
    /// <param name="sink">The sink.</param>
    /// <param name="inner">The condition.</param>
    internal static void WriteInside<TInner>(ref ExpressionSink sink, in TInner inner)
        where TInner : ICondition
    {
        var term = sink._text.WrittenCount;
        inner.Render(ref sink);
        sink.Unwrap(term, ExpressionGroup.None);
    }

    // Takes the parentheses off a term that is the group written last, for a group of this operator or, with None, of any.
    private readonly void Unwrap(int term, ExpressionGroup group)
    {
        if (_groupStart == term && _groupEnd == _text.WrittenCount && (group == ExpressionGroup.None || group == _group))
        {
            _text.Unwrap(term);
        }
    }
}
