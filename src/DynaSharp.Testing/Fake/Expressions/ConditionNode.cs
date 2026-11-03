namespace DynaSharp.Testing.Fake;

/// <summary>A parsed condition, the tree of a <c>ConditionExpression</c>, a <c>FilterExpression</c> or a <c>KeyConditionExpression</c>. Parentheses leave no node of their own.</summary>
internal abstract record ConditionNode
{
    private ConditionNode()
    {
    }

    /// <summary><c>left AND right</c>.</summary>
    /// <param name="Left">The left condition.</param>
    /// <param name="Right">The right condition.</param>
    public sealed record And(ConditionNode Left, ConditionNode Right) : ConditionNode;

    /// <summary><c>left OR right</c>.</summary>
    /// <param name="Left">The left condition.</param>
    /// <param name="Right">The right condition.</param>
    public sealed record Or(ConditionNode Left, ConditionNode Right) : ConditionNode;

    /// <summary><c>NOT inner</c>.</summary>
    /// <param name="Inner">The condition.</param>
    public sealed record Not(ConditionNode Inner) : ConditionNode;

    /// <summary><c>left = right</c> and the other comparators.</summary>
    /// <param name="Comparator">The comparator.</param>
    /// <param name="Left">The left operand.</param>
    /// <param name="Right">The right operand.</param>
    public sealed record Compare(Comparator Comparator, Operand Left, Operand Right) : ConditionNode;

    /// <summary><c>value BETWEEN lower AND upper</c>, both ends included.</summary>
    /// <param name="Value">The operand tested.</param>
    /// <param name="Lower">The lower end.</param>
    /// <param name="Upper">The upper end.</param>
    public sealed record Between(Operand Value, Operand Lower, Operand Upper) : ConditionNode;

    /// <summary><c>value IN (candidate, ...)</c>.</summary>
    /// <param name="Value">The operand tested.</param>
    /// <param name="Candidates">The candidates, 1 to 100.</param>
    public sealed record In(Operand Value, IReadOnlyList<Operand> Candidates) : ConditionNode;

    /// <summary>A function that is a condition: <c>attribute_exists(a)</c>.</summary>
    /// <param name="Kind">The function.</param>
    /// <param name="Name">The name as the expression has it, for messages.</param>
    /// <param name="Operands">The operands, as many as the function takes.</param>
    public sealed record Function(ConditionFunction Kind, string Name, IReadOnlyList<Operand> Operands) : ConditionNode;
}
