using System.Diagnostics;
using System.Numerics;
using DynaSharp.Expressions;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// An attribute of an item without a schema, by name or document path, as <see cref="Expr.Attr(string)"/> makes it. The conditions it makes
/// are the ones DynamoDB has for any attribute; it also goes into a hole of an interpolated expression, where it stands for the name.
/// </summary>
/// <remarks>
/// <para>
/// A path is names joined by dots, with list indexes in brackets: <c>status</c>, <c>address.city</c>, <c>lines[2].sku</c>. Every name becomes a
/// placeholder, so reserved words such as <c>status</c> or <c>size</c> need no care. A dot always separates names, so an attribute whose
/// name contains a dot cannot be addressed this way.
/// </para>
/// <para>
/// Values convert as <see cref="AttributeValue"/> does, except numbers, which go through without the string a conversion would cost.
/// </para>
/// <para>It converts to its path, so it goes wherever a method takes a path as a string.</para>
/// </remarks>
[DebuggerDisplay("{Path,nq}")]
public readonly struct Attr
{
    /// <summary>Initializes a new instance of the <see cref="Attr"/> struct.</summary>
    /// <param name="path">The name or document path.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public Attr(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    /// <summary>Gets the name or document path.</summary>
    public string Path { get; }

    /// <summary>Converts the attribute to its path.</summary>
    /// <param name="attribute">The attribute.</param>
    public static implicit operator string(Attr attribute) => attribute.Path;

    /// <summary>Compares with <c>=</c>. DynamoDB compares by type first: a string never equals a number.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<AttributeValue, AttributeValueConverter> Eq(AttributeValue value) => new(Path, ComparisonOperator.Equal, value);

    /// <summary>Compares with <c>=</c>. DynamoDB compares numbers by value: <c>1</c> equals <c>1.0</c>.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="value">The number.</param>
    /// <returns>The condition.</returns>
    public Compare<TNumber, NumberConverter<TNumber>> Eq<TNumber>(TNumber value)
        where TNumber : INumberBase<TNumber> => new(Path, ComparisonOperator.Equal, value);

    /// <summary>Compares with <c>&lt;&gt;</c>. It holds for an attribute that does not exist.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<AttributeValue, AttributeValueConverter> Ne(AttributeValue value) => new(Path, ComparisonOperator.NotEqual, value);

    /// <summary>Compares with <c>&lt;&gt;</c>. It holds for an attribute that does not exist.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="value">The number.</param>
    /// <returns>The condition.</returns>
    public Compare<TNumber, NumberConverter<TNumber>> Ne<TNumber>(TNumber value)
        where TNumber : INumberBase<TNumber> => new(Path, ComparisonOperator.NotEqual, value);

    /// <summary>Compares with <c>&lt;</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<AttributeValue, AttributeValueConverter> Lt(AttributeValue value) => new(Path, ComparisonOperator.Less, value);

    /// <summary>Compares with <c>&lt;</c>.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="value">The number.</param>
    /// <returns>The condition.</returns>
    public Compare<TNumber, NumberConverter<TNumber>> Lt<TNumber>(TNumber value)
        where TNumber : INumberBase<TNumber> => new(Path, ComparisonOperator.Less, value);

    /// <summary>Compares with <c>&lt;=</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<AttributeValue, AttributeValueConverter> Le(AttributeValue value) => new(Path, ComparisonOperator.LessOrEqual, value);

    /// <summary>Compares with <c>&lt;=</c>.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="value">The number.</param>
    /// <returns>The condition.</returns>
    public Compare<TNumber, NumberConverter<TNumber>> Le<TNumber>(TNumber value)
        where TNumber : INumberBase<TNumber> => new(Path, ComparisonOperator.LessOrEqual, value);

    /// <summary>Compares with <c>&gt;</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<AttributeValue, AttributeValueConverter> Gt(AttributeValue value) => new(Path, ComparisonOperator.Greater, value);

    /// <summary>Compares with <c>&gt;</c>.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="value">The number.</param>
    /// <returns>The condition.</returns>
    public Compare<TNumber, NumberConverter<TNumber>> Gt<TNumber>(TNumber value)
        where TNumber : INumberBase<TNumber> => new(Path, ComparisonOperator.Greater, value);

    /// <summary>Compares with <c>&gt;=</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<AttributeValue, AttributeValueConverter> Ge(AttributeValue value) => new(Path, ComparisonOperator.GreaterOrEqual, value);

    /// <summary>Compares with <c>&gt;=</c>.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="value">The number.</param>
    /// <returns>The condition.</returns>
    public Compare<TNumber, NumberConverter<TNumber>> Ge<TNumber>(TNumber value)
        where TNumber : INumberBase<TNumber> => new(Path, ComparisonOperator.GreaterOrEqual, value);

    /// <summary>Checks for a range, both bounds included. DynamoDB rejects a low bound above the high bound.</summary>
    /// <param name="low">The low bound.</param>
    /// <param name="high">The high bound.</param>
    /// <returns>The condition.</returns>
    public Between<AttributeValue, AttributeValueConverter> Between(AttributeValue low, AttributeValue high) => new(Path, low, high);

    /// <summary>Checks for a range, both bounds included. DynamoDB rejects a low bound above the high bound.</summary>
    /// <typeparam name="TNumber">The type of the numbers.</typeparam>
    /// <param name="low">The low bound.</param>
    /// <param name="high">The high bound.</param>
    /// <returns>The condition.</returns>
    public Between<TNumber, NumberConverter<TNumber>> Between<TNumber>(TNumber low, TNumber high)
        where TNumber : INumberBase<TNumber> => new(Path, low, high);

    /// <summary>Checks with <c>attribute_exists</c>. An attribute with the DynamoDB null value exists.</summary>
    /// <returns>The condition.</returns>
    public AttributeExists Exists() => new(Path, exists: true);

    /// <summary>Checks with <c>attribute_not_exists</c>. An attribute with the DynamoDB null value exists.</summary>
    /// <returns>The condition.</returns>
    public AttributeExists NotExists() => new(Path, exists: false);

    /// <summary>Checks with <c>begins_with</c>, which is for strings and compares bytes.</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The condition.</returns>
    public BeginsWith BeginsWith(string prefix) => new(Path, prefix);

    /// <summary>
    /// Checks with <c>contains</c>: a string that holds the value as a substring, a set that holds it as an element, which takes a value of the element type of the set, or a list that holds an element
    /// equal to it. An attribute that does not exist holds nothing, so the condition fails there and its <c>Negate()</c> holds.
    /// </summary>
    /// <param name="operand">The substring or the element.</param>
    /// <returns>The condition.</returns>
    public Contains<AttributeValue, AttributeValueConverter> Contains(AttributeValue operand) => new(Path, operand);

    /// <summary>Checks with <c>contains</c> whether a number set or a list holds a number, which goes through without the string a conversion would cost.</summary>
    /// <typeparam name="TNumber">The type of the number.</typeparam>
    /// <param name="operand">The number.</param>
    /// <returns>The condition.</returns>
    public Contains<TNumber, NumberConverter<TNumber>> Contains<TNumber>(TNumber operand)
        where TNumber : INumberBase<TNumber> => new(Path, operand);

    /// <summary>Returns the path.</summary>
    /// <returns>The path.</returns>
    public override string ToString() => Path;
}
