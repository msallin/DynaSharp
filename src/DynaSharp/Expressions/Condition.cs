using DynaSharp.Expressions;

namespace DynaSharp;

/// <summary>
/// A condition that can be kept in a field, assembled at run time and combined with <c>&amp;</c>, <c>|</c> and <c>!</c>: <c>Condition.Of(Attr("status").Eq("open"))</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>When to use it.</b> The conditions that <see cref="Attr"/> and the generated <c>Attrs</c> make are structs whose type changes with every <c>.And()</c>, <c>.Or()</c> and
/// <c>.Negate()</c>, which is awkward for a field or a condition built in a loop. <see cref="Of{T}"/> wraps one in this type, whose type stays the same.
/// </para>
/// <para>
/// <b>Operators.</b> <c>a.Eq(1) &amp; b.Gt(2)</c> does not compile on two structs: <c>&amp;</c>, <c>|</c> and <c>!</c> need a <see cref="Condition"/> on one side, and the struct on the other side
/// converts, as in <c>.If(Open &amp; Attr("version").Eq(v))</c>. Instances are immutable, so one can be shared between threads and commands.
/// </para>
/// </remarks>
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

    /// <summary>Wraps a condition in a <see cref="Condition"/> object, so it can be kept in a field, assembled at run time or combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.</summary>
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
