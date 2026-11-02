using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A condition on the sort key of a query. Only what DynamoDB allows in a key condition can be expressed: one comparison, a range or a
/// prefix, and no <c>&lt;&gt;</c>. The default value means no condition, which reads the whole partition.
/// </summary>
/// <remarks>
/// A generated key builder has these conditions for its template, from the values of its placeholders: <c>Order.Keys.Sk.Between(from, to)</c>. The ones here are for a value that is at hand already, such as a
/// sort key of a table without a mapped type. Strings compare by their UTF-8 bytes, so a condition on the text of a composite key means what those bytes mean.
/// </remarks>
public readonly struct SortKeyCondition
{
    private readonly Kind _kind;
    private readonly AttributeValue _first;
    private readonly AttributeValue _second;

    private SortKeyCondition(Kind kind, AttributeValue first, AttributeValue second = default)
    {
        _kind = kind;
        _first = first;
        _second = second;
    }

    private enum Kind
    {
        None,
        Equal,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
        Between,
        BeginsWith,
    }

    /// <summary>Gets a value indicating whether there is a condition.</summary>
    internal bool IsSet => _kind != Kind.None;

    /// <summary>Selects the one sort key equal to a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public static SortKeyCondition Eq(AttributeValue value) => new(Kind.Equal, value);

    /// <summary>Selects sort keys below a value. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public static SortKeyCondition Lt(AttributeValue value) => new(Kind.Less, value);

    /// <summary>Selects sort keys up to a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public static SortKeyCondition Le(AttributeValue value) => new(Kind.LessOrEqual, value);

    /// <summary>Selects sort keys above a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public static SortKeyCondition Gt(AttributeValue value) => new(Kind.Greater, value);

    /// <summary>Selects sort keys from a value on.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public static SortKeyCondition Ge(AttributeValue value) => new(Kind.GreaterOrEqual, value);

    /// <summary>Selects sort keys in a range, both bounds included.</summary>
    /// <param name="low">The low bound.</param>
    /// <param name="high">The high bound.</param>
    /// <returns>The condition.</returns>
    public static SortKeyCondition Between(AttributeValue low, AttributeValue high) => new(Kind.Between, low, high);

    /// <summary>Selects sort keys that start with a prefix, the way to read one kind of item of a partition.</summary>
    /// <param name="prefix">The prefix, for example <c>ORDER#</c>.</param>
    /// <returns>The condition.</returns>
    public static SortKeyCondition BeginsWith(string prefix) => new(Kind.BeginsWith, prefix);

    /// <summary>Appends the condition to a key condition that holds the partition key already.</summary>
    /// <param name="writer">The expressions of the command.</param>
    /// <param name="text">The text of the key condition.</param>
    /// <param name="sortKeyName">The attribute name of the sort key.</param>
    internal void Render(ExpressionWriter writer, PooledByteBuffer text, string sortKeyName)
    {
        if (_kind == Kind.None)
        {
            return;
        }

        text.Append(" AND "u8);
        if (_kind == Kind.BeginsWith)
        {
            text.Append("begins_with("u8);
            writer.AppendAttributeName(text, sortKeyName);
            text.Append(", "u8);
            writer.AppendValue<AttributeValue, AttributeValueConverter>(text, _first);
            text.Append((byte)')');
            return;
        }

        writer.AppendAttributeName(text, sortKeyName);
        text.Append(_kind switch
        {
            Kind.Equal => " = "u8,
            Kind.Less => " < "u8,
            Kind.LessOrEqual => " <= "u8,
            Kind.Greater => " > "u8,
            Kind.GreaterOrEqual => " >= "u8,
            _ => " BETWEEN "u8,
        });
        writer.AppendValue<AttributeValue, AttributeValueConverter>(text, _first);
        if (_kind == Kind.Between)
        {
            text.Append(" AND "u8);
            writer.AppendValue<AttributeValue, AttributeValueConverter>(text, _second);
        }
    }
}
