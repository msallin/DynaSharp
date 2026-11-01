using System.Diagnostics;
using DynaSharp.Expressions;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// An attribute with a .NET type and the converter that writes it. The source generator emits one per property (<c>Customer.Attrs.Email</c>),
/// <see cref="Expr.Attr{T}(string)"/> makes one for a number, and any other can be created by hand. A value of the wrong type is a compile
/// error. The converter is a type argument because writing a value is then a direct call, with no delegate and no box.
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

    /// <summary>Makes a value of this attribute for a hole of an interpolated expression: <c>$"{attr} = {attr.Value(x)}"</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value with its converter.</returns>
    public Arg<T, TConverter> Value(T value) => new(value);

    /// <summary>Returns the path.</summary>
    /// <returns>The path.</returns>
    public override string ToString() => Path;
}
