using System.Diagnostics.CodeAnalysis;

namespace DynaSharp.Expressions;

/// <summary>
/// A condition as a value, which is what <c>If</c> takes. Composition with <c>And</c> and <c>Or</c> builds nested generic structs, so a condition
/// allocates nothing, at the price of a static type that changes with every step. A condition assembled at run time, or kept in a field,
/// goes through <see cref="Condition"/>.
/// </summary>
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
