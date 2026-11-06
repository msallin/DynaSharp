using System.Diagnostics;
using DynaSharp.Expressions;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// An attribute with a .NET type and the converter that writes it. The source generator emits one per property (<c>Customer.Attrs.Email</c>),
/// <see cref="Expr.Attr{T}(string)"/> makes one for a number, and any other can be created by hand. A value of the wrong type is a compile
/// error. The docs call such an attribute a handle: <c>Order.Attrs.Total</c> is the handle of the <c>Total</c> property.
/// </summary>
/// <remarks>The path has the syntax described on <see cref="Attr"/>.</remarks>
/// <typeparam name="T">The .NET type.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
[DebuggerDisplay("{Path,nq}")]
public readonly struct Attr<T, TConverter>
    where TConverter : IDynamoDbConverter<T>
{
    /// <summary>Initializes a new instance of the <see cref="Attr{T, TConverter}"/> struct.</summary>
    /// <param name="path">The name or document path.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public Attr(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    /// <summary>Gets the name or document path.</summary>
    public string Path { get; }

    /// <summary>
    /// Converts the attribute to one without a type, for what takes attributes of different types, such as a projection. There is no conversion
    /// to a string: it would turn a value of the wrong type in <c>Set(attribute, value)</c> into a call with a path that compiles.
    /// </summary>
    /// <param name="attribute">The attribute.</param>
    public static implicit operator Attr(Attr<T, TConverter> attribute) => new(attribute.Path);

    /// <summary>Compares with <c>=</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<T, TConverter> Eq(T value) => new(Path, ComparisonOperator.Equal, value);

    /// <summary>Compares with <c>&lt;&gt;</c>. It holds for an attribute that does not exist.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<T, TConverter> Ne(T value) => new(Path, ComparisonOperator.NotEqual, value);

    /// <summary>Compares with <c>&lt;</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<T, TConverter> Lt(T value) => new(Path, ComparisonOperator.Less, value);

    /// <summary>Compares with <c>&lt;=</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<T, TConverter> Le(T value) => new(Path, ComparisonOperator.LessOrEqual, value);

    /// <summary>Compares with <c>&gt;</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<T, TConverter> Gt(T value) => new(Path, ComparisonOperator.Greater, value);

    /// <summary>Compares with <c>&gt;=</c>. Strings compare by their UTF-8 bytes.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The condition.</returns>
    public Compare<T, TConverter> Ge(T value) => new(Path, ComparisonOperator.GreaterOrEqual, value);

    /// <summary>Checks for a range, both bounds included. DynamoDB rejects a low bound above the high bound.</summary>
    /// <param name="low">The low bound.</param>
    /// <param name="high">The high bound.</param>
    /// <returns>The condition.</returns>
    public Between<T, TConverter> Between(T low, T high) => new(Path, low, high);

    /// <summary>Checks with <c>attribute_exists</c>. An attribute with the DynamoDB null value exists.</summary>
    /// <returns>The condition.</returns>
    public AttributeExists Exists() => new(Path, exists: true);

    /// <summary>Checks with <c>attribute_not_exists</c>. An attribute with the DynamoDB null value exists.</summary>
    /// <returns>The condition.</returns>
    public AttributeExists NotExists() => new(Path, exists: false);

    /// <summary>
    /// Checks that the attribute is missing or below a value, <c>(attribute_not_exists(a) OR a &lt; :v)</c>: the guard of a write that must never move an item back.
    /// </summary>
    /// <remarks>
    /// It is <c>NotExists().Or(Lt(value))</c>. <c>.If(Order.Attrs.PlacedAt.NotExistsOrLt(order.PlacedAt))</c> stores an event only when the item holds an older one, or none, so an event that
    /// arrives late or twice fails the condition. An item that does not exist passes, and so does one without the attribute. The DynamoDB null value is an attribute that exists and compares
    /// with nothing, so an item that holds it fails.
    /// </remarks>
    /// <param name="value">The value the stored one has to be below, such as the version or the time of the event that is written.</param>
    /// <returns>The condition.</returns>
    public Or<AttributeExists, Compare<T, TConverter>> NotExistsOrLt(T value) => new(NotExists(), Lt(value));

    /// <summary>
    /// Checks that the attribute is missing or at most a value, <c>(attribute_not_exists(a) OR a &lt;= :v)</c>, which is <c>NotExists().Or(Le(value))</c>: the guard of something free or expired, such as a
    /// lease whose end is at or before now. See <see cref="NotExistsOrLt(T)"/>.
    /// </summary>
    /// <param name="value">The value the stored one may not exceed.</param>
    /// <returns>The condition.</returns>
    public Or<AttributeExists, Compare<T, TConverter>> NotExistsOrLe(T value) => new(NotExists(), Le(value));

    /// <summary>Makes a value of this attribute for a hole of an interpolated expression: <c>$"{attr} = {attr.Value(x)}"</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value with its converter.</returns>
    public Arg<T, TConverter> Value(T value) => new(value);

    /// <summary>Returns the path.</summary>
    /// <returns>The path.</returns>
    public override string ToString() => Path;
}
