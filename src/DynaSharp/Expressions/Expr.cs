using System.Numerics;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// The vocabulary of expressions without a schema, for <c>using static DynaSharp.Expr;</c>: <c>Attr("status")</c> and <c>Attr&lt;int&gt;("orderCount")</c>.
/// Struct conditions cover comparisons, existence and <c>begins_with</c>, and an interpolated expression covers the rest of the grammar:
/// <c>$"size({Attr("tags")}) &lt; {10}"</c>.
/// </summary>
public static class Expr
{
    /// <summary>Makes an attribute without a type, which takes any value.</summary>
    /// <param name="path">The name or document path, such as <c>status</c>, <c>address.city</c> or <c>lines[2].sku</c>.</param>
    /// <returns>The attribute.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public static Attr Attr(string path) => new(path);

    /// <summary>
    /// Makes a number attribute, the form to use with <c>Return(...)</c> and a typed <c>Add</c>. For other types, a generated handle has the right
    /// converter, and <see cref="Attr{T, TConverter}"/> can be created by hand.
    /// </summary>
    /// <typeparam name="T">The number type.</typeparam>
    /// <param name="path">The name or document path.</param>
    /// <returns>The attribute.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public static Attr<T, NumberConverter<T>> Attr<T>(string path)
        where T : INumberBase<T> => new(path);
}
