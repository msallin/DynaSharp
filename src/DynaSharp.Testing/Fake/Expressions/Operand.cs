namespace DynaSharp.Testing.Fake;

/// <summary>
/// An operand of a parsed expression: what a comparison compares, what a function takes, what <c>SET</c> assigns. A condition holds <see cref="Attribute"/>, <see cref="Constant"/>
/// and <see cref="Size"/>; the <c>SET</c> section of an update holds <see cref="Attribute"/>, <see cref="Constant"/>, <see cref="IfNotExists"/>, <see cref="ListAppend"/> and <see cref="Sum"/>.
/// </summary>
internal abstract record Operand
{
    private Operand()
    {
    }

    /// <summary>Gets the value of a constant operand, or <see langword="null"/> for any other.</summary>
    public AttributeValue? ConstantValue => this is Constant constant ? constant.Value : (AttributeValue?)null;

    /// <summary>A document path: <c>a.b[0]</c>.</summary>
    /// <param name="Path">The path.</param>
    public sealed record Attribute(DocumentPath Path) : Operand;

    /// <summary>A value placeholder, resolved: <c>:v</c>.</summary>
    /// <param name="Value">The value, in its stored form.</param>
    public sealed record Constant(AttributeValue Value) : Operand;

    /// <summary><c>size(operand)</c>, a number.</summary>
    /// <param name="Inner">The operand it measures.</param>
    public sealed record Size(Operand Inner) : Operand;

    /// <summary><c>if_not_exists(path, operand)</c>: the value at the path, or the operand where there is none.</summary>
    /// <param name="Path">The path.</param>
    /// <param name="Fallback">The operand.</param>
    public sealed record IfNotExists(DocumentPath Path, Operand Fallback) : Operand;

    /// <summary><c>list_append(first, second)</c>: the elements of both lists, the first ones first.</summary>
    /// <param name="First">The first list.</param>
    /// <param name="Second">The second list.</param>
    public sealed record ListAppend(Operand First, Operand Second) : Operand;

    /// <summary><c>left + right</c> or <c>left - right</c>, of two numbers.</summary>
    /// <param name="Left">The left operand.</param>
    /// <param name="Subtract">Whether it is <c>-</c>.</param>
    /// <param name="Right">The right operand.</param>
    public sealed record Sum(Operand Left, bool Subtract, Operand Right) : Operand;
}
