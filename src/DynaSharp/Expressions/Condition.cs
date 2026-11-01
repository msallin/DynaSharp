using DynaSharp.Expressions;

namespace DynaSharp;

/// <summary>
/// A condition on the heap, for one that is kept in a field or assembled at run time, where the changing static type of the struct
/// conditions gets in the way. Struct conditions compose with <c>.And()</c>, <c>.Or()</c> and <c>.Negate()</c>, and <c>a.Eq(1) &amp; b.Gt(2)</c> does not compile: <c>&amp;</c>, <c>|</c> and <c>!</c> need a
/// <see cref="Condition"/>, which a struct on the other side then converts to, as in <c>.If(Open &amp; Attr("version").Eq(v))</c>. Instances are immutable, so one can be shared between threads and commands.
/// </summary>
public abstract class Condition : ICondition
{
    private Condition()
    {
    }

    /// <summary>Makes a condition that holds when both hold.</summary>
    /// <param name="left">The first condition.</param>
    /// <param name="right">The second condition.</param>
    /// <returns>The condition.</returns>
    public static Condition operator &(Condition left, Condition right) => Of(new And<Condition, Condition>(left, right));

    /// <summary>Makes a condition that holds when either holds.</summary>
    /// <param name="left">The first condition.</param>
    /// <param name="right">The second condition.</param>
    /// <returns>The condition.</returns>
    public static Condition operator |(Condition left, Condition right) => Of(new Or<Condition, Condition>(left, right));

    /// <summary>Makes a condition that holds when another one does not.</summary>
    /// <param name="condition">The other condition.</param>
    /// <returns>The opposite.</returns>
    public static Condition operator !(Condition condition) => Of(new Not<Condition>(condition));

    /// <summary>Puts a condition on the heap.</summary>
    /// <typeparam name="T">The type of the condition.</typeparam>
    /// <param name="condition">The struct condition.</param>
    /// <returns>The condition, in one object.</returns>
    public static Condition Of<T>(T condition)
        where T : ICondition => new Boxed<T>(condition);

    /// <inheritdoc/>
    public abstract void Render(ref ExpressionSink sink);

    private sealed class Boxed<T>(T condition) : Condition
        where T : ICondition
    {
        public override void Render(ref ExpressionSink sink) => condition.Render(ref sink);
    }
}
