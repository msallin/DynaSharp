using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Expressions;

/// <summary>
/// A condition as a value, which is what <c>If</c> and <c>Filter</c> take: <c>Order.Attrs.Status.Eq(OrderStatus.Open)</c>, <c>Attr("total").Gt(100)</c>.
/// </summary>
/// <remarks>
/// <c>.And()</c>, <c>.Or()</c> and <c>.Negate()</c> combine conditions into a new struct whose type reflects its parts, so the type changes with every step.
/// A condition that is kept in a field or assembled at run time goes through <see cref="Condition"/>, whose type stays the same.
/// </remarks>
public interface ICondition
{
    /// <summary>
    /// Writes the condition as expression text. Attribute names and values go through <see cref="ExpressionSink.Path"/> and
    /// <see cref="ExpressionSink.Value{T, TConverter}"/>, which turn them into placeholders.
    /// </summary>
    /// <param name="sink">Where the text goes.</param>
    [SuppressMessage("Design", "CA1045:Do not pass types by reference", Justification = "A ref struct that interface methods take, so it cannot be a generic parameter; by reference it stays one instance.")]
    public void Render(ref ExpressionSink sink);
}
